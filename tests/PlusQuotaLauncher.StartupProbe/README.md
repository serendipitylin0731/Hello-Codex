# 安装后重启验证

此探针调用与正式程序相同的 `PostInstallRestart`，用于在已登录 Windows 桌面上验证真实进程交接。只读取额度，不发送任务。

1. 将项目以自包含方式发布到一个新的临时目录：
   `dotnet publish tests/PlusQuotaLauncher.StartupProbe -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/startup-probe`
2. 执行 `PlusQuotaLauncher.StartupProbe.exe --simulate-installer "Codex CLI 的完整路径"`。
3. 检查输出目录中的 `probe-result.json`：安装器环境标记不得继承，`RestartRequested` 必须为 `false`，额度窗口应为 300 分钟。
4. `probe-completions.txt` 应只有一行；`probe-bootstrap.json` 中的安装器和交接进程应已退出。

正式探针进程完成后会保留 5 秒，便于使用任务管理器或 `Win32_Process` 检查其父进程是否为 `explorer.exe`。每次验证应使用新的输出目录，避免读取上次结果。没有 Windows 桌面的 CI 只运行核心回归测试。
