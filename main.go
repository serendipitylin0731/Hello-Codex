// Codex Quota Pinger - Windows-local Codex App Server scheduler.
// Uses only the Go standard library. No OpenAI API key is required;
// sign into the official Codex CLI with a ChatGPT account first.
package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"time"
)

const version = "1.0.0"
const thresholdSeconds = 4*3600 + 59*60 // STRICTLY greater

type Config struct {
	CodexPath            string `json:"codex_path"`
	LimitID              string `json:"limit_id"`
	Model                string `json:"model"`
	Effort               string `json:"effort"`
	Message              string `json:"message"`
	PollSeconds          int    `json:"poll_seconds"`
	SuccessSleepSeconds  int    `json:"success_sleep_seconds"`
	DisabledUTCStartHour int    `json:"disabled_utc_start_hour"`
	DisabledUTCEndHour   int    `json:"disabled_utc_end_hour"`
}

func defaultConfig() Config {
	return Config{CodexPath: "codex", LimitID: "codex", Model: "gpt-5.6-luna",
		Effort: "low", Message: "1", PollSeconds: 120, SuccessSleepSeconds: 600,
		DisabledUTCStartHour: 16, DisabledUTCEndHour: 21}
}

func loadConfig(path string) (Config, error) {
	cfg := defaultConfig()
	data, err := os.ReadFile(path)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return cfg, nil
		}
		return cfg, err
	}
	if err := json.Unmarshal(data, &cfg); err != nil {
		return cfg, err
	}
	if cfg.CodexPath == "" || cfg.LimitID == "" || cfg.Model == "" || cfg.Effort == "" || cfg.Message == "" || cfg.PollSeconds < 1 || cfg.SuccessSleepSeconds < 1 || cfg.DisabledUTCStartHour < 0 || cfg.DisabledUTCStartHour > 23 || cfg.DisabledUTCEndHour < 0 || cfg.DisabledUTCEndHour > 23 || cfg.DisabledUTCStartHour == cfg.DisabledUTCEndHour {
		return cfg, fmt.Errorf("invalid config.json; check the non-empty fields, intervals and UTC hours")
	}
	return cfg, nil
}

func disabledUTC(now time.Time, start, end int) bool {
	h := now.UTC().Hour()
	if start < end {
		return h >= start && h < end
	}
	return h >= start || h < end
}

// State is written in an atomic rename, so a restart preserves deduplication.
// PendingResetAt prevents a duplicate when the server accepted turn/start but
// a network/process failure prevented us from receiving its acknowledgment.
type State struct {
	LastSuccessResetAt int64  `json:"last_success_reset_at"`
	LastSuccessUnix    int64  `json:"last_success_unix"`
	LastThreadID       string `json:"last_thread_id"`
	PendingResetAt     int64  `json:"pending_reset_at"`
}

func readState(path string) (State, error) {
	var s State
	b, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return s, err
	}
	return s, json.Unmarshal(b, &s)
}
func writeState(path string, s State) error {
	b, err := json.MarshalIndent(s, "", "  ")
	if err != nil {
		return err
	}
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, append(b, '\n'), 0600); err != nil {
		return err
	}
	return replaceFile(tmp, path)
}

type limitWindow struct {
	UsedPercent        *float64 `json:"usedPercent"`
	WindowDurationMins *int     `json:"windowDurationMins"`
	ResetsAt           *int64   `json:"resetsAt"`
}
type snapshot struct {
	LimitID   string       `json:"limitId"`
	Primary   *limitWindow `json:"primary"`
	Secondary *limitWindow `json:"secondary"`
}
type rateResult struct {
	RateLimits snapshot            `json:"rateLimits"`
	ByLimitID  map[string]snapshot `json:"rateLimitsByLimitId"`
}

func select5hWindow(r rateResult, limitID string) (limitWindow, bool) {
	snap, ok := r.ByLimitID[limitID]
	if !ok {
		if r.RateLimits.LimitID != "" && r.RateLimits.LimitID != limitID {
			return limitWindow{}, false
		}
		snap = r.RateLimits
	}
	for _, w := range []*limitWindow{snap.Primary, snap.Secondary} {
		if w != nil && w.WindowDurationMins != nil && *w.WindowDurationMins == 300 && w.ResetsAt != nil && *w.ResetsAt > 0 {
			return *w, true
		}
	}
	return limitWindow{}, false
}

type rpcError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
}
type rpcResponse struct {
	Result json.RawMessage `json:"result"`
	Error  *rpcError       `json:"error"`
}
type appServer struct {
	cmd      *exec.Cmd
	input    io.WriteCloser
	mu       sync.Mutex
	nextID   int64
	pending  map[int64]chan rpcResponse
	closed   chan struct{}
	stopOnce sync.Once
	logger   *log.Logger
}

func newAppServer(codexPath string, logger *log.Logger) (*appServer, error) {
	var cmd *exec.Cmd
	// npm global installs often expose codex.cmd on Windows, not codex.exe.
	path, err := exec.LookPath(codexPath)
	if err != nil {
		return nil, fmt.Errorf("Codex CLI missing (%q): %w; install Codex and run codex login", codexPath, err)
	}
	if runtime.GOOS == "windows" && (strings.HasSuffix(strings.ToLower(path), ".cmd") || strings.HasSuffix(strings.ToLower(path), ".bat")) {
		shell := os.Getenv("ComSpec")
		if shell == "" {
			shell = "cmd.exe"
		}
		// The batch-file path is an already resolved executable, never arbitrary user input.
		cmd = exec.Command(shell, "/d", "/c", `"`+path+`" app-server --listen stdio://`)
	} else {
		cmd = exec.Command(path, "app-server", "--listen", "stdio://")
	}
	input, err := cmd.StdinPipe()
	if err != nil {
		return nil, err
	}
	output, err := cmd.StdoutPipe()
	if err != nil {
		return nil, err
	}
	cmd.Stderr = os.Stderr // leave diagnostics visible; don't log credentials to disk.
	if err := cmd.Start(); err != nil {
		return nil, err
	}
	c := &appServer{cmd: cmd, input: input, pending: make(map[int64]chan rpcResponse), closed: make(chan struct{}), logger: logger}
	go c.readLoop(output)
	go func() {
		err := cmd.Wait()
		if err != nil {
			logger.Printf("Codex app-server exited: %v", err)
		}
	}()
	var initResult map[string]interface{}
	err = c.call("initialize", map[string]any{
		"clientInfo": map[string]string{"name": "local_quota_pinger", "title": "Local Quota Pinger", "version": version},
	}, &initResult, 25*time.Second)
	if err != nil {
		c.stop()
		return nil, fmt.Errorf("Codex initialize: %w", err)
	}
	if err := c.notify("initialized", map[string]any{}); err != nil {
		c.stop()
		return nil, err
	}
	return c, nil
}

func (c *appServer) readLoop(output io.Reader) {
	defer close(c.closed)
	scanner := bufio.NewScanner(output)
	scanner.Buffer(make([]byte, 64*1024), 16*1024*1024)
	for scanner.Scan() {
		line := scanner.Bytes()
		var envelope struct {
			ID     *int64          `json:"id"`
			Result json.RawMessage `json:"result"`
			Error  *rpcError       `json:"error"`
		}
		if json.Unmarshal(line, &envelope) != nil || envelope.ID == nil {
			continue
		} // Ignore streamed replies/notifications.
		c.mu.Lock()
		ch, ok := c.pending[*envelope.ID]
		if ok {
			delete(c.pending, *envelope.ID)
		}
		c.mu.Unlock()
		if ok {
			ch <- rpcResponse{Result: envelope.Result, Error: envelope.Error}
		}
	}
	if err := scanner.Err(); err != nil {
		c.logger.Printf("Codex stdout read failed: %v", err)
	}
}

func (c *appServer) sendLocked(v any) error {
	b, err := json.Marshal(v)
	if err != nil {
		return err
	}
	b = append(b, '\n')
	_, err = c.input.Write(b)
	return err
}
func (c *appServer) notify(method string, params any) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.sendLocked(map[string]any{"method": method, "params": params})
}
func (c *appServer) call(method string, params any, dst any, timeout time.Duration) error {
	c.mu.Lock()
	c.nextID++
	id := c.nextID
	ch := make(chan rpcResponse, 1)
	c.pending[id] = ch
	err := c.sendLocked(map[string]any{"id": id, "method": method, "params": params})
	c.mu.Unlock()
	if err != nil {
		c.mu.Lock()
		delete(c.pending, id)
		c.mu.Unlock()
		return err
	}
	timer := time.NewTimer(timeout)
	defer timer.Stop()
	select {
	case reply := <-ch:
		if reply.Error != nil {
			return fmt.Errorf("RPC %s [%d]: %s", method, reply.Error.Code, reply.Error.Message)
		}
		if len(reply.Result) == 0 {
			return fmt.Errorf("RPC %s: empty result", method)
		}
		if dst == nil {
			return nil
		}
		return json.Unmarshal(reply.Result, dst)
	case <-c.closed:
		return fmt.Errorf("Codex server disconnected during %s", method)
	case <-timer.C:
		c.mu.Lock()
		delete(c.pending, id)
		c.mu.Unlock()
		return fmt.Errorf("RPC %s timed out after %s", method, timeout)
	}
}
func (c *appServer) isClosed() bool {
	select {
	case <-c.closed:
		return true
	default:
		return false
	}
}
func (c *appServer) stop() {
	c.stopOnce.Do(func() {
		_ = c.input.Close()
		// No need to wait for model completion when the scheduler itself exits.
		if c.cmd.Process != nil {
			_ = c.cmd.Process.Kill()
		}
	})
}

func canSend(now time.Time, resetAt int64, cfg Config) bool {
	return !disabledUTC(now, cfg.DisabledUTCStartHour, cfg.DisabledUTCEndHour) &&
		time.Unix(resetAt, 0).Sub(now) > time.Duration(thresholdSeconds)*time.Second
}

func once(now time.Time, cfg Config, statePath string, logger *log.Logger, client **appServer, dryRun bool, workdir string) bool {
	if disabledUTC(now, cfg.DisabledUTCStartHour, cfg.DisabledUTCEndHour) {
		logger.Printf("DISABLED UTC=%s; no requests sent", now.UTC().Format("15:04:05"))
		return false
	}
	s, err := readState(statePath)
	if err != nil {
		logger.Printf("ERROR state.json: %v (safe skip)", err)
		return false
	}
	if *client == nil || (*client).isClosed() {
		if *client != nil {
			(*client).stop()
		}
		*client, err = newAppServer(cfg.CodexPath, logger)
		if err != nil {
			logger.Printf("ERROR %v", err)
			return false
		}
	}
	var data rateResult
	if err := (*client).call("account/rateLimits/read", map[string]any{}, &data, 35*time.Second); err != nil {
		logger.Printf("ERROR read rate limits: %v", err)
		return false
	}
	window, ok := select5hWindow(data, cfg.LimitID)
	if !ok {
		logger.Printf("SKIP no 300-minute window with resetsAt under limit_id=%q; check Codex login/plan/bucket", cfg.LimitID)
		return false
	}
	resetAt := *window.ResetsAt
	remaining := time.Until(time.Unix(resetAt, 0)).Round(time.Second)
	logger.Printf("CHECK 5h quota reset=%s remaining=%s (trigger only if >4h59m)", time.Unix(resetAt, 0).UTC().Format(time.RFC3339), remaining)
	if s.LastSuccessResetAt == resetAt {
		logger.Printf("SKIP this reset window already sent successfully, thread=%s", s.LastThreadID)
		return false
	}
	if s.PendingResetAt == resetAt {
		logger.Printf("SKIP previous turn/start for this window had uncertain outcome; inspect state.json and log before retrying")
		return false
	}
	if !canSend(time.Now(), resetAt, cfg) {
		return false
	}
	if dryRun {
		logger.Printf("DRY RUN: would send text=%q model=%s effort=%s", cfg.Message, cfg.Model, cfg.Effort)
		return false
	}
	var threadResp struct {
		Thread struct {
			ID string `json:"id"`
		} `json:"thread"`
	}
	if err := (*client).call("thread/start", map[string]any{
		"model": cfg.Model, "cwd": workdir, "approvalPolicy": "never", "sandbox": "readOnly", "serviceName": "local_quota_pinger",
	}, &threadResp, 30*time.Second); err != nil {
		logger.Printf("ERROR thread/start: %v", err)
		return false
	}
	if threadResp.Thread.ID == "" {
		logger.Printf("ERROR thread/start missing ID")
		return false
	}
	// Time may have crossed the UTC disabled boundary during thread creation.
	if !canSend(time.Now(), resetAt, cfg) {
		logger.Printf("SKIP window/UTC conditions changed before turn/start")
		return false
	}
	s.PendingResetAt = resetAt
	if err := writeState(statePath, s); err != nil {
		logger.Printf("ERROR writing pending state: %v (safe skip)", err)
		return false
	}
	var turnResp struct {
		Turn struct {
			ID     string `json:"id"`
			Status string `json:"status"`
		} `json:"turn"`
	}
	err = (*client).call("turn/start", map[string]any{
		"threadId": threadResp.Thread.ID,
		"input":    []map[string]string{{"type": "text", "text": cfg.Message}},
		"model":    cfg.Model, "effort": cfg.Effort, "cwd": workdir,
		"approvalPolicy": "never", "sandboxPolicy": map[string]string{"type": "readOnly"},
	}, &turnResp, 30*time.Second)
	if err != nil {
		logger.Printf("ERROR turn/start: %v", err)
		// If the server explicitly rejected the request, it was not accepted.
		// With timeout/disconnect we cannot know: keep PendingResetAt to avoid duplicates.
		if strings.Contains(err.Error(), "RPC turn/start [") {
			s.PendingResetAt = 0
			if saveErr := writeState(statePath, s); saveErr != nil {
				logger.Printf("ERROR saving state: %v", saveErr)
			}
		}
		return false
	}
	if turnResp.Turn.ID == "" || strings.EqualFold(turnResp.Turn.Status, "failed") || strings.EqualFold(turnResp.Turn.Status, "interrupted") {
		logger.Printf("ERROR turn/start not accepted (turn_id=%q status=%q)", turnResp.Turn.ID, turnResp.Turn.Status)
		s.PendingResetAt = 0
		if saveErr := writeState(statePath, s); saveErr != nil {
			logger.Printf("ERROR saving state: %v", saveErr)
		}
		return false
	}
	s.LastSuccessResetAt = resetAt
	s.LastSuccessUnix = time.Now().Unix()
	s.LastThreadID = threadResp.Thread.ID
	s.PendingResetAt = 0
	if err := writeState(statePath, s); err != nil {
		logger.Printf("WARNING accepted but saving state failed: %v", err)
	}
	logger.Printf("SENT successfully accepted: message=%q model=%s effort=%s thread=%s turn=%s; sleeping %d seconds (no reply handling)", cfg.Message, cfg.Model, cfg.Effort, threadResp.Thread.ID, turnResp.Turn.ID, cfg.SuccessSleepSeconds)
	return true
}

func main() {
	onceFlag := flag.Bool("once", false, "perform one check then exit")
	dryRun := flag.Bool("dry-run", false, "read real limits without sending a message")
	versionFlag := flag.Bool("version", false, "print version")
	inspectFlag := flag.Bool("inspect", false, "list all rate limit buckets and windows without sending")
	flag.Parse()
	if *versionFlag {
		fmt.Println("Codex Quota Pinger", version)
		return
	}
	exe, err := os.Executable()
	if err != nil {
		log.Fatal(err)
	}
	home := filepath.Dir(exe)
	configPath := filepath.Join(home, "config.json")
	cfg, err := loadConfig(configPath)
	if err != nil {
		log.Fatalf("config error: %v", err)
	}
	unlock, err := singleInstanceLock()
	if err != nil {
		log.Fatalf("cannot start a second copy: %v", err)
	}
	defer unlock()
	logFile := filepath.Join(home, "pinger.log")
	if st, err := os.Stat(logFile); err == nil && st.Size() > 5*1024*1024 {
		_ = replaceFile(logFile, logFile+".old")
	}
	f, err := os.OpenFile(logFile, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0600)
	if err != nil {
		log.Fatal(err)
	}
	defer f.Close()
	logger := log.New(io.MultiWriter(os.Stdout, f), "[QuotaPinger] ", log.LstdFlags|log.LUTC)
	workdir := filepath.Join(home, "codex_workspace")
	if err := os.MkdirAll(workdir, 0700); err != nil {
		logger.Fatal(err)
	}
	statePath := filepath.Join(home, "state.json")
	var client *appServer
	defer func() {
		if client != nil {
			client.stop()
		}
	}()
	logger.Printf("START version=%s target=Codex only, UTC disabled=%02d:00–%02d:00, check=%ds, success sleep=%ds, model=%s effort=%s", version, cfg.DisabledUTCStartHour, cfg.DisabledUTCEndHour, cfg.PollSeconds, cfg.SuccessSleepSeconds, cfg.Model, cfg.Effort)
	if *inspectFlag {
		c, e := newAppServer(cfg.CodexPath, logger)
		if e != nil {
			logger.Printf("ERROR %v", e)
			return
		}
		defer c.stop()
		var result rateResult
		if e := c.call("account/rateLimits/read", map[string]any{}, &result, 35*time.Second); e != nil {
			logger.Printf("ERROR inspect: %v", e)
			return
		}
		if len(result.ByLimitID) == 0 {
			printSnapshot(logger, "default", result.RateLimits)
		}
		for name, snap := range result.ByLimitID {
			printSnapshot(logger, name, snap)
		}
		return
	}
	for {
		sent := once(time.Now(), cfg, statePath, logger, &client, *dryRun, workdir)
		if *onceFlag {
			// Keep app-server alive if a turn was accepted; closing it immediately
			// could abort the model request before it actually executes.
			if sent {
				time.Sleep(time.Duration(cfg.SuccessSleepSeconds) * time.Second)
			}
			return
		}
		if sent {
			time.Sleep(time.Duration(cfg.SuccessSleepSeconds) * time.Second)
		} else {
			time.Sleep(time.Duration(cfg.PollSeconds) * time.Second)
		}
	}
}

func printSnapshot(logger *log.Logger, name string, snap snapshot) {
	for _, item := range []struct {
		name   string
		window *limitWindow
	}{{"primary", snap.Primary}, {"secondary", snap.Secondary}} {
		if item.window == nil {
			continue
		}
		duration := "unknown"
		reset := "unknown"
		if item.window.WindowDurationMins != nil {
			duration = fmt.Sprintf("%dm", *item.window.WindowDurationMins)
		}
		if item.window.ResetsAt != nil {
			reset = time.Unix(*item.window.ResetsAt, 0).UTC().Format(time.RFC3339)
		}
		logger.Printf("BUCKET %q %s duration=%s reset_utc=%s", name, item.name, duration, reset)
	}
}
