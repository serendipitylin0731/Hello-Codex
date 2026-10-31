# 后台启动界面验证

在已登录 Windows 桌面的环境中运行：

```powershell
dotnet run --project tests/HelloCodex.UiProbe/HelloCodex.UiProbe.csproj -c Release
```

探针使用临时设置且不启动额度监控，检查后台启动时没有创建或显示主窗口、托盘图标存在，以及从托盘恢复窗口后窗口可见。窗口在屏幕外显示以避免打扰当前操作。
