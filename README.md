# ChatGPT Plus 额度启动器

[![CI](https://github.com/serendipitylin0731/codex-preuse/actions/workflows/ci.yml/badge.svg)](https://github.com/serendipitylin0731/codex-preuse/actions/workflows/ci.yml)

<p align="center"><img src="assets/logo.png" width="128" alt="ChatGPT Plus 额度启动器 Logo"></p>

这是一个为 **ChatGPT Plus 订阅用户**设计的 Windows Codex 额度启动器。程序通过本机 Codex App Server 读取五小时额度窗口，并按你设置的时区、免打扰时段和额度重置条件启动任务。

本项目是独立的开源工具，与 OpenAI 无隶属关系，也未获得 OpenAI 背书。

## 主要功能

- 现代 WPF 图形界面与一体化标题栏，支持高 DPI 和 Windows 10/11
- 支持跟随系统、浅色和深色三种外观模式
- 显示五小时额度使用率、重置时间、登录类型和订阅计划
- 可选择 Windows 时区并设置免打扰时段，支持跨午夜
- 自动监控，也可以在确认后手动发送
- 模型、思考强度、额度桶、Codex CLI 和检查间隔集中在折叠的高级设置中
- 系统托盘常驻；关闭窗口时可选择隐藏到后台或完全退出
- 可选随 Windows 登录自动启动
- 同一额度窗口防重复，超时或断线时保守处理不确定请求
- 界面显示简明错误，诊断日志保留完整技术信息
- 不需要 OpenAI API Key，也不会保存 ChatGPT 密码

## 安装

1. 从 [GitHub Releases](https://github.com/serendipitylin0731/codex-preuse/releases/latest) 下载 `ChatGPTPlusQuotaLauncherSetup.exe`。
2. 运行安装程序。默认安装到当前用户目录，不需要管理员权限。
3. 安装官方 Codex CLI，并登录 ChatGPT Plus 账户：

   ```powershell
   npm install -g @openai/codex@latest
   codex login
   codex --version
   ```

4. 打开额度启动器，选择时区和免打扰时段。
5. 点击“立即检查”，确认界面能够显示 ChatGPT Plus 和五小时额度。

安装包目前是未进行商业代码签名的社区构建，Windows SmartScreen 可能要求确认。请在 Release 页面核对 SHA-256 后再运行。

## 默认调度规则

| 项目 | 默认值 |
|---|---|
| 时区 | UTC，可在界面修改 |
| 免打扰时段 | `[16:00, 21:00)` |
| 检查间隔 | 120 秒 |
| 额度桶 | `codex` |
| 目标窗口 | 300 分钟 |
| 自动触发条件 | 距离 `resetsAt` 严格大于 4 小时 59 分钟 |
| 模型 | `gpt-5.6-luna` |
| 思考强度 | `low` |
| 发送内容 | `1` |

> 默认 120 秒检查间隔大于一分钟的触发区间，因此可能错过某个新窗口。需要时可在高级设置中缩短间隔，最低为 30 秒。

## 手动发送

“手动发送”会先保存并校验当前设置，再显示确认窗口。确认后会：

- 忽略当前免打扰时段；
- 忽略自动触发阈值；
- 使用界面中设置的模型、思考强度和发送内容；
- 消耗 ChatGPT Plus/Codex 使用额度。

程序确认 App Server 接受任务后即返回，不读取或展示模型最终回答。

## 后台运行与文件位置

- 程序运行时始终显示系统托盘图标。
- 双击托盘图标可恢复主窗口。
- 点击关闭按钮时，可以选择隐藏到后台、完全退出或取消。
- 勾选“随 Windows 登录自动启动”后，程序会写入当前用户的 Windows Run 项。

设置、状态与日志保存在：

```text
%LOCALAPPDATA%\ChatGPTPlusQuotaLauncher\
```

其中包含：

- `settings.json`：界面与调度设置；
- `state.json`：自动发送去重状态；
- `launcher.log`：完整诊断日志；
- `codex_workspace\`：Codex 任务使用的本地工作目录。

## 从源码构建

需要 Windows 和 .NET 8 SDK：

```powershell
dotnet build src/PlusQuotaLauncher/PlusQuotaLauncher.csproj -c Release
dotnet run --project tests/PlusQuotaLauncher.Core.Tests/PlusQuotaLauncher.Core.Tests.csproj -c Release
dotnet publish src/PlusQuotaLauncher/PlusQuotaLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

GitHub Actions 会运行回归测试、生成自包含 Windows x64 应用，并使用经过签名验证的 Inno Setup 生成单个安装程序。Release 只发布安装包，不发布项目 ZIP。

## 官方资料

- [ChatGPT 与 Codex 计划及用量](https://learn.chatgpt.com/docs/pricing)
- [Codex App Server 协议](https://learn.chatgpt.com/docs/app-server)
- [Codex 开发者命令](https://learn.chatgpt.com/docs/developer-commands)

许可信息见 [LICENSE.txt](LICENSE.txt)。
