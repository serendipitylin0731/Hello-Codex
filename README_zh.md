# Codex Quota Pinger（Windows x64）

本程序用于 **Codex 本地 App Server**。它不是对 ChatGPT 网页输入框的自动点击，也不会向已有 ChatGPT Work 会话发消息；触发时会创建新的 Codex 会话，并将字符串 `1` 作为用户输入发送。

## 先决条件

1. Windows 10/11 x64。需要运行时，电脑应保持开机、联网；系统休眠期间不会触发。
2. 安装官方 **Codex CLI** 并使用 **ChatGPT 账户**登录：

   ```powershell
   npm install -g @openai/codex@latest
   codex login
   codex --version
   ```

   如果 `codex` 不在 PATH 中，请修改 `config.json` 的 `codex_path`，填写 `codex.exe` 或 `codex.cmd` 的完整路径。

3. 账户需要能通过 App Server 读取一个 `windowDurationMins=300` 且带 `resetsAt` 的额度窗口，并能在 Codex 中使用 `gpt-5.6-luna`。API 模型目录中存在该模型，并不保证每个 ChatGPT/Codex 计划都有访问权限。程序不会改用付费 API Key 调用。

## 快速开始

1. 将 ZIP **完整解压到普通用户可写目录**，例如 `C:\Users\<你>\CodexQuotaPinger\`。不要只单独运行 EXE，也不要放在通常需要管理员权限的 `Program Files` 下。
2. 双击 `inspect_limits.bat`：只显示账户可见额度窗口，不发送消息。寻找 `duration=300m` 并确认 `BUCKET` 名称；如果不是 `codex`，将其填入 `config.json` 的 `limit_id`。
3. 双击 `check_once.bat`：读取真实额度并执行一次 **dry run**；即使满足触发条件也不会发送。
4. 双击 `start.bat` 持续在前台运行，或双击 `start_hidden.vbs` 在后台运行。进程退出后调度停止，日志位于同一目录的 `pinger.log`。
5. 如需登录 Windows 后自启动：按 `Win+R`，输入 `shell:startup`，在打开的启动文件夹中创建指向 `start_hidden.vbs` 的快捷方式。

## 固定规则

| 条件 | 行为 |
|---|---|
| UTC 16:00:00 至 20:59:59 | 禁止发送，也不查询额度 |
| 其他 UTC 时间 | 每 120 秒查询一次 Codex 额度 |
| `windowDurationMins=300` 且 `resetsAt - 当前时间` **严格大于** 17940 秒 | 创建 Codex 会话，使用 `gpt-5.6-luna`、`low`，输入 `1` |
| App Server 确认接受 `turn/start` | 等待 600 秒，不读取或管理模型回复 |
| 未满足条件或请求失败 | 120 秒后再次检查 |
| 同一额度窗口已经成功发送 | 跳过，防止重复 |

重要：2 分钟轮询间隔 **大于** 1 分钟触发区间，所以可能完全错过某次新窗口。这是需求规则的直接结果。进入禁用时段前已经接受的后台任务不会被本程序中止。

## 配置

`config.json` 默认内容：

```json
{
  "codex_path": "codex",
  "limit_id": "codex",
  "model": "gpt-5.6-luna",
  "effort": "low",
  "message": "1",
  "poll_seconds": 120,
  "success_sleep_seconds": 600,
  "disabled_utc_start_hour": 16,
  "disabled_utc_end_hour": 21
}
```

修改配置后需要重启程序。

## 日志与状态

- `pinger.log`：UTC 日志；超过 5 MB 时轮换为 `pinger.log.old`。
- `state.json`：记录最后成功提交对应的重置时间和 Codex thread ID，程序重启后仍可防重复。
- `pending_reset_at`：请求已开始提交，但超时或断线导致结果不确定时保留。请先检查日志并确认远端结果，再考虑手动将其设为 `0`。
- `turn/start` 被接受不代表模型已经完成，也不保证额度窗口已经刷新。程序按要求不读取最终回复。
- 没有 5 小时窗口、窗口未启动、缺少重置时间或 CLI 未登录时，程序会安全跳过，不猜测重置时间。

## 手动排查

```powershell
.\CodexQuotaPinger.exe -version
.\CodexQuotaPinger.exe -inspect
.\CodexQuotaPinger.exe -once -dry-run
```

- `Codex CLI missing`：确认同一 Windows 用户的 PowerShell 可以运行 `codex --version`；修改 PATH 后请重新打开终端。
- `SKIP no 300-minute window`：检查 `-inspect` 列出的桶名与窗口时长，必要时修改 `limit_id`。不要选择不相关的额度桶。
- 模型无权限：账户的 Codex 可能没有 `gpt-5.6-luna`。程序不会静默回退到其他模型。
- `another CodexQuotaPinger is already running`：已有实例持有单实例锁，请先在任务管理器中确认现有进程。

## 源码与构建

项目仅使用 Go 标准库，无第三方运行时依赖；运行时仍需要 Codex CLI。安装 Go 1.23 或更高版本后：

```powershell
go test ./...
go build -trimpath -o CodexQuotaPinger.exe .
```

官方协议文档：<https://learn.chatgpt.com/docs/app-server>

模型文档：<https://developers.openai.com/api/docs/models/gpt-5.6-luna>
