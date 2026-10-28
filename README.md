# ChatGPT Plus Quota Launcher

[![CI](https://github.com/serendipitylin0731/codex-preuse/actions/workflows/ci.yml/badge.svg)](https://github.com/serendipitylin0731/codex-preuse/actions/workflows/ci.yml)

<p align="center"><img src="assets/logo.png" width="128" alt="ChatGPT Plus Quota Launcher logo"></p>

A native Windows desktop utility designed for ChatGPT Plus subscribers who use Codex. It reads the account's five-hour Codex quota window through the local Codex App Server and can start a lightweight task when your configured schedule and reset conditions match.

This is an independent open-source utility. It is not affiliated with or endorsed by OpenAI.

## Highlights

- Native Windows UI with clear account, quota, reset, and error reporting
- Selectable Windows time zone and configurable disabled time range, including overnight ranges
- Automatic monitoring plus an explicitly confirmed **Send now** action
- System tray icon and close-window prompt: keep running in the background, exit, or cancel
- Optional per-user startup with Windows
- Persistent deduplication and conservative handling of uncertain requests
- No API key and no stored ChatGPT password; it uses the locally authenticated Codex CLI
- Single EXE installer in every GitHub Release

## Install

1. Download `ChatGPTPlusQuotaLauncherSetup.exe` from [Releases](https://github.com/serendipitylin0731/codex-preuse/releases).
2. Run the installer. Administrator access is not required for the default per-user install.
3. Install the official Codex CLI and run `codex login` with your ChatGPT account.
4. Open the launcher, choose your time zone and disabled hours, then select **Check now**.

The installer and application are currently unsigned community builds, so Windows SmartScreen may ask for confirmation. Verify the SHA-256 digest shown in the GitHub Release before running it.

## Default scheduling rule

The default remains compatible with the original project specification:

- quota bucket: `codex`
- target window: 300 minutes
- check interval: 120 seconds
- disabled range: UTC `[16:00, 21:00)`
- trigger: reset time is strictly more than 4 hours 59 minutes away
- model: `gpt-5.6-luna`
- reasoning effort: `low`
- message: `1`

The model, effort, message, time zone, disabled range, CLI path, quota bucket, and check interval can be changed in the UI.

## Build

Requires the .NET 8 SDK on Windows:

```powershell
dotnet build src/PlusQuotaLauncher/PlusQuotaLauncher.csproj -c Release
dotnet run --project tests/PlusQuotaLauncher.Core.Tests/PlusQuotaLauncher.Core.Tests.csproj -c Release
dotnet publish src/PlusQuotaLauncher/PlusQuotaLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

The release workflow verifies and uses Inno Setup 7 to produce one installer EXE.

## Official references

- [ChatGPT and Codex pricing/plan usage](https://learn.chatgpt.com/docs/pricing)
- [Codex App Server protocol](https://learn.chatgpt.com/docs/app-server)
- [Codex developer commands](https://learn.chatgpt.com/docs/developer-commands)

See [README_zh.md](README_zh.md) for the complete Chinese guide.
