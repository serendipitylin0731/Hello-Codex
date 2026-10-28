# Codex Quota Pinger

[![CI](https://github.com/serendipitylin0731/codex-preuse/actions/workflows/ci.yml/badge.svg)](https://github.com/serendipitylin0731/codex-preuse/actions/workflows/ci.yml)

一个面向 Windows 10/11 x64 的本地 Codex 配额调度器。它通过官方 Codex App Server 读取 5 小时额度窗口；满足指定时间条件时，创建新的 Codex 会话并发送 `1`。

它不会模拟网页点击，不会向已有 ChatGPT Work 对话注入消息，也不需要额外的 OpenAI API Key。Codex CLI 必须已经使用有相应权限的 ChatGPT 账户登录。

## 行为规则

| 项目 | 默认设置 |
|---|---|
| 禁用时段 | UTC `[16:00, 21:00)` |
| 检查间隔 | 120 秒 |
| 目标额度窗口 | `limit_id=codex` 且 `windowDurationMins=300` |
| 触发条件 | 距离 `resetsAt` 严格大于 4 小时 59 分钟 |
| 模型 | `gpt-5.6-luna` |
| 思考强度 | `low` |
| 消息 | `1` |
| 成功提交后等待 | 600 秒 |

同一 `resetsAt` 最多提交一次。若 `turn/start` 超时或断线、无法确认是否已经到达服务端，程序会保留 pending 标记并安全跳过，避免重复发送。

> 120 秒的轮询周期大于 1 分钟触发区间，因此可能错过某个新窗口。这是当前规则的直接结果。

## 快速开始

1. 从 [Releases](https://github.com/serendipitylin0731/codex-preuse/releases) 下载 Windows ZIP 并完整解压到普通用户可写目录。
2. 安装官方 Codex CLI，并运行 `codex login` 登录 ChatGPT 账户。
3. 双击 `inspect_limits.bat`，确认存在 `duration=300m` 的额度桶；如果桶名不是 `codex`，修改 `config.json` 的 `limit_id`。
4. 双击 `check_once.bat` 做一次真实额度读取和 dry run；该命令不会发送消息。
5. 双击 `start.bat` 前台运行，或双击 `start_hidden.vbs` 后台运行。

详细配置、日志、状态文件、自启动方法及故障排查见 [README_zh.md](README_zh.md)。

## 从源码构建

需要 Go 1.23 或更高版本：

```powershell
go test ./...
go build -trimpath -o CodexQuotaPinger.exe .
```

CI 会在 Windows 与 Linux 上运行测试、交叉验证 Windows 构建，并为 `v*` 标签生成 GitHub Release ZIP。

## 官方资料

- [Codex App Server 协议](https://learn.chatgpt.com/docs/app-server)
- [Codex CLI 命令](https://learn.chatgpt.com/docs/developer-commands)
- [GPT-5.6 Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)

## 许可

见 [LICENSE.txt](LICENSE.txt)。本项目不是 OpenAI 官方产品，也未获得 OpenAI 背书。
