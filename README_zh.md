# Hello! Codex

<p align="center"><img src="assets/logo.png" width="128" alt="Hello! Codex Logo"></p>

这是一个为 **ChatGPT Plus 订阅用户**设计的 Windows Codex 额度启动器。程序通过本机 Codex App Server 读取账户返回的五小时额度窗口，在满足你设置的时区、每日启用时间与重置条件时，向同一个 Codex 会话发送指定内容。

本项目是独立的开源工具，与 OpenAI 无隶属关系，也未获得 OpenAI 背书。

## 主要功能

- 原生 Windows 图形界面，显示 ChatGPT 登录类型、订阅计划、五小时额度、使用率和下次重置时间
- 现代 WPF 一体化界面，支持跟随系统、浅色和深色外观
- 时区默认跟随系统，也可选择任意 Windows 时区
- 默认每天相同的启用时间轴；点击“每日定制”后可分别设置周一到周日
- 每天可添加多个启用区间，拖动时间轴端点或输入时间，保存时自动合并重叠区间
- 自动监控，也可点击“手动发送”立即要求发送
- 手动发送会忽略启用时间和额度阈值，但每次都会显示确认窗口
- 系统托盘常驻图标；双击图标恢复主窗口
- 关闭窗口时询问：隐藏到后台、完全退出或取消关闭
- 可选择随 Windows 登录自动启动，并以后台托盘模式运行
- 同一额度窗口防重复；超时或断线时保守记录不确定状态，避免盲目重复发送
- Codex 连接中断时自动重连并重试额度读取；日志时间跟随所选时区并标注时区
- 界面、托盘通知和日志会明确报告错误原因、处理建议与日志路径
- 不需要 OpenAI API Key，也不会保存 ChatGPT 密码；使用本机已经登录的 Codex CLI

## 安装

1. 从 [GitHub Releases](https://github.com/serendipitylin0731/Hello-Codex/releases) 下载 `HelloCodexSetup.exe`。
2. 运行安装程序。默认安装到当前用户目录，不要求管理员权限。
3. 安装官方 Codex CLI，并使用 ChatGPT 账户登录：

   ```powershell
   npm install -g @openai/codex@latest
   codex login
   codex --version
   ```

4. 打开“Hello! Codex”，设置时区和启用时间。
5. 点击“立即检查”，确认界面显示 ChatGPT 登录以及 300 分钟额度窗口。
6. 保存设置并启动监控。

当前安装包是未进行商业代码签名的社区构建，Windows SmartScreen 可能要求确认。运行前请核对 Release 页面提供的 SHA-256。

## 默认规则

| 项目 | 默认值 |
|---|---|
| 目标用户 | ChatGPT Plus 订阅用户 |
| 时区 | 跟随系统，可在界面修改 |
| 启用时间 | 默认每天 `[00:00, 16:00)` 与 `[21:00, 24:00)`；旧版禁用时段会自动转换 |
| 检查间隔 | 120 秒 |
| 额度桶 | `codex` |
| 目标窗口 | 300 分钟 |
| 触发条件 | 距离 `resetsAt` 严格大于 4 小时 59 分钟 |
| 模型 | `gpt-5.6-luna` |
| 思考强度 | `low` |
| 发送内容 | `1` |

模型、思考强度、发送内容、时区、启用时间、Codex CLI 路径、额度桶和检查间隔都可以在界面修改。

> 120 秒轮询周期大于 1 分钟触发区间，因此自动模式可能错过某个新窗口。这是当前默认规则的直接结果。需要时可在界面缩短检查间隔，最低为 30 秒。

## 手动发送

点击“手动发送”后，程序会先保存并校验当前设置，再显示确认窗口。选择“是”后，程序会在自动发送共用的会话中发送内容。手动发送：

- 忽略当前是否处于启用时间；
- 忽略额度重置阈值；
- 使用界面中的模型、思考强度和发送内容；
- 会消耗 ChatGPT Plus/Codex 使用额度；
- 不等待或读取模型的最终回答，只确认任务是否已被 App Server 接受。

## 后台运行与开机启动

- 程序运行时始终显示系统托盘图标。
- 双击托盘图标或选择“显示主窗口”可恢复界面。
- 点击窗口关闭按钮时，程序询问是否隐藏到后台或完全退出。
- 勾选“随 Windows 登录自动启动”并保存后，程序写入当前用户的 Windows Run 项；取消勾选会移除它。
- 安装程序也提供一个可选的开机启动选项。

## 文件位置

程序数据保存在：

```text
%LOCALAPPDATA%\Hello! Codex\
```

从旧版升级时，首次启动会复制原有的设置和自动发送去重状态；原目录留作备份。旧版开机启动项也会自动指向新版程序。

其中：

- `settings.json`：界面设置；
- `state.json`：自动发送去重、不确定状态与复用的会话 ID；
- `launcher.log`：详细日志；每次程序启动时清空，使用所选时区并标注 UTC 偏移和时区标识；
- `codex_workspace\`：只读 Codex 任务使用的本地工作目录。

## 常见问题

### 未找到 Codex CLI

确认 PowerShell 中能运行 `codex --version`。也可在界面的“Codex CLI”字段选择 `codex.exe` 或 `codex.cmd` 的完整路径。

### 显示的登录不是 ChatGPT 或计划不是 Plus

应用会显示 App Server 实际返回的登录类型和计划。请执行 `codex logout` 后重新运行 `codex login`，选择正确的 ChatGPT Plus 账户。应用不会把 API Key 额度当作 Plus 订阅额度。

### 找不到 300 分钟窗口

确认额度桶名称正确，默认是 `codex`。额度窗口由账户和当前 Codex 服务返回；程序不会猜测重置时间。

### 手动发送或自动发送失败

界面会显示可操作的错误说明。点击“诊断日志”查看技术细节，并检查：

- Codex CLI 是否为最新版本；
- ChatGPT Plus 登录是否仍有效；
- 当前账户是否有权使用设置的模型；
- 网络或安全软件是否阻止 `codex app-server`。

## 开发与构建

需要 Windows 和 .NET 8 SDK：

```powershell
dotnet build src/HelloCodex/HelloCodex.csproj -c Release
dotnet run --project tests/HelloCodex.Core.Tests/HelloCodex.Core.Tests.csproj -c Release
dotnet publish src/HelloCodex/HelloCodex.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

GitHub Actions 会运行回归测试、生成自包含 Windows x64 应用，并通过经过签名验证的 Inno Setup 7 生成一个安装程序 EXE。Release 只发布安装包，不再发布整个 ZIP。

## 官方资料

- <https://learn.chatgpt.com/docs/pricing>
- <https://learn.chatgpt.com/docs/app-server>
- <https://learn.chatgpt.com/docs/developer-commands>
