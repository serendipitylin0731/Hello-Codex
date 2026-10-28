package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestMain(m *testing.M) {
	if os.Getenv("CODEX_QUOTA_PINGER_TEST_HELPER") == "1" {
		runMockAppServer()
		os.Exit(0)
	}
	os.Exit(m.Run())
}

func runMockAppServer() {
	scanner := bufio.NewScanner(os.Stdin)
	for scanner.Scan() {
		var request map[string]json.RawMessage
		if json.Unmarshal(scanner.Bytes(), &request) != nil {
			continue
		}
		var method string
		_ = json.Unmarshal(request["method"], &method)
		if method == "initialized" || len(request["id"]) == 0 {
			continue
		}

		var result any
		switch method {
		case "initialize":
			result = map[string]any{"userAgent": "mock"}
		case "account/rateLimits/read":
			result = map[string]any{"rateLimitsByLimitId": map[string]any{
				"codex": map[string]any{
					"limitId": "codex",
					"primary": map[string]any{
						"usedPercent": 0, "windowDurationMins": 300,
						"resetsAt": time.Now().Unix() + 300*60,
					},
				},
			}}
		case "thread/start":
			result = map[string]any{"thread": map[string]any{"id": "mock-thread"}}
		case "turn/start":
			result = map[string]any{"turn": map[string]any{"id": "mock-turn", "status": "inProgress"}}
		default:
			continue
		}
		response, _ := json.Marshal(map[string]any{"id": json.RawMessage(request["id"]), "result": result})
		fmt.Println(string(response))
	}
}

func TestDisabledUTCBoundaries(t *testing.T) {
	tests := []struct {
		h, m int
		want bool
	}{{15, 59, false}, {16, 0, true}, {20, 59, true}, {21, 0, false}, {0, 0, false}}
	for _, tt := range tests {
		got := disabledUTC(time.Date(2026, 10, 28, tt.h, tt.m, 0, 0, time.UTC), 16, 21)
		if got != tt.want {
			t.Errorf("%02d:%02d got %v want %v", tt.h, tt.m, got, tt.want)
		}
	}
}
func TestStrictThreshold(t *testing.T) {
	cfg := defaultConfig()
	now := time.Date(2026, 10, 28, 10, 0, 0, 0, time.UTC)
	if canSend(now, now.Add(4*time.Hour+59*time.Minute).Unix(), cfg) {
		t.Fatal("must not send at exactly 4h59")
	}
	if !canSend(now, now.Add(4*time.Hour+59*time.Minute+time.Second).Unix(), cfg) {
		t.Fatal("must send at >4h59")
	}
	if canSend(time.Date(2026, 10, 28, 16, 0, 0, 0, time.UTC), time.Date(2026, 10, 28, 21, 0, 0, 0, time.UTC).Unix(), cfg) {
		t.Fatal("disabled")
	}
}
func TestSelectionIsFiveHoursAndOnlySelectedBucket(t *testing.T) {
	var r rateResult
	raw := `{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"windowDurationMins":300,"resetsAt":1793200000},"secondary":{"windowDurationMins":10080,"resetsAt":1793800000}},"other":{"primary":{"windowDurationMins":300,"resetsAt":2}}}}`
	if err := json.Unmarshal([]byte(raw), &r); err != nil {
		t.Fatal(err)
	}
	w, ok := select5hWindow(r, "codex")
	if !ok || *w.ResetsAt != 1793200000 {
		t.Fatalf("bad selection: %+v %v", w, ok)
	}
	if _, ok := select5hWindow(r, "not-listed"); ok {
		t.Fatal("unselected bucket accepted")
	}
}
func TestMissingWindowSkips(t *testing.T) {
	var r rateResult
	if _, ok := select5hWindow(r, "codex"); ok {
		t.Fatal("empty quota should not trigger")
	}
	raw := `{"rateLimits":{"limitId":"codex","primary":{"windowDurationMins":60,"resetsAt":1793200000},"secondary":{"windowDurationMins":300}}}`
	_ = json.Unmarshal([]byte(raw), &r)
	if _, ok := select5hWindow(r, "codex"); ok {
		t.Fatal("missing reset timestamp should not trigger")
	}
}

func TestOnceSubmitsExpectedTurnAndPersistsState(t *testing.T) {
	t.Setenv("CODEX_QUOTA_PINGER_TEST_HELPER", "1")
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	tempDir := t.TempDir()
	cfg := defaultConfig()
	cfg.CodexPath = executable
	now := time.Now().UTC()
	cfg.DisabledUTCStartHour = (now.Hour() + 1) % 24
	cfg.DisabledUTCEndHour = (now.Hour() + 2) % 24

	logger := log.New(io.Discard, "", 0)
	var client *appServer
	t.Cleanup(func() {
		if client != nil {
			client.stop()
		}
	})
	statePath := filepath.Join(tempDir, "state.json")
	if !once(now, cfg, statePath, logger, &client, false, tempDir) {
		t.Fatal("expected mock turn to be accepted")
	}
	state, err := readState(statePath)
	if err != nil {
		t.Fatal(err)
	}
	if state.LastThreadID != "mock-thread" || state.LastSuccessResetAt == 0 || state.PendingResetAt != 0 {
		t.Fatalf("unexpected persisted state: %+v", state)
	}
}
