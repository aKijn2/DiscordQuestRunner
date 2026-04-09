# Discord Quest Runner - Developer Documentation

Welcome to the development side of **Discord Quest Runner**. This document outlines the technical stack, build instructions, roadmap, and how the Chrome DevTools Protocol (CDP) bridge works.

For general user instructions, please see the [main README.md](README.md).

---

## Technical Stack

- **Framework**: .NET MAUI 9
- **Language**: C# 13, XAML
- **Runtime Scripts**: Vanilla JavaScript (ES6+)
- **Bridge / Injection**: CDP (Chrome DevTools Protocol) over local debug port (`127.0.0.1:9222`)

### How it works
The app operates by connecting to Discord's embedded Chromium instance via its remote debugging port. It locates the active web socket target and injects obfuscated/minified JavaScript directly into the Discord client. The injected JavaScript interacts with Discord's internal Webpack modules (`webpackChunkdiscord_app`) to tap into the UI stores (FluxDispatcher, QuestsStore, ChannelStore, etc.) and mock game presences, stream states, or video watching analytics.

---

## Build from Source

### Prerequisites
- [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **.NET MAUI development** workload.
- .NET 9.0 SDK.

### Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/aKijn2/DiscordQuestRunner.git
   cd DiscordQuestRunner
   ```

2. Restore and run the Windows target:
   ```bash
   dotnet run -f net9.0-windows10.0.19041.0
   ```

*(Note: While MAUI supports multiple platforms, the CDP injection currently relies on Windows desktop process detection [e.g., `Process.GetProcessesByName("Discord")`] and local app data paths).*

---

## Project Structure

The workspace is now organized by responsibility instead of mixing pages, assets, and helper code at the project root:

```text
DiscordQuestRunner/
|- README.md
|- DEVELOPMENT.md
|- docs/
|  \- assets/
|- DiscordQuestRunner/
|  |- App.xaml
|  |- App.xaml.cs
|  |- MauiProgram.cs
|  |- Pages/
|  |  |- QuestRunnerPage.xaml
|  |  \- MessagePurgePage.xaml
|  |- Services/
|  |  \- Discord/
|  |     \- DiscordService.cs
|  |- Interop/
|  |  \- Windows/
|  |     \- WindowHelper.cs
|  |- Resources/
|  |  \- Raw/
|  |     \- Automation/
|  \- Platforms/
```

### Folder intent

- `Pages/`: All user-facing MAUI screens and their code-behind.
- `Services/Discord/`: Runtime automation, CDP connection logic, script execution, and health checks.
- `Interop/Windows/`: Native Windows-only helpers that should stay separate from cross-platform code.
- `Resources/Raw/Automation/`: Bundled JavaScript payloads injected into Discord at runtime.
- `docs/assets/`: Screenshots and documentation-only media that should not ship with the app binary.

---

## Roadmap

- [x] **Auto-Accept Quests**: Automatically accept currently valid visible quests.
- [x] **STATS ROW**: Implement stats row logic.
- [x] **Captcha Handling**: Improve workflow around manual/automated captcha interruptions.
- [x] **Fix Watch Video**: Progress logic refactored to allow unhindered loop and robust CDP payload synchronization.
- [x] **Preflight Environment Check**: Run a fast local readiness check before execution to validate Discord process state, CDP availability, active target discovery, and required Webpack store access.
- [ ] **Auto-Reattach Session Recovery**: Recover cleanly from Discord restarts, websocket drops, or transient CDP disconnects without forcing a full manual restart.
- [ ] **Adaptive Retry and Jitter Control**: Add bounded retry/backoff and small execution jitter around sensitive automation steps to improve stability and reduce brittle timing patterns.
- [ ] **Injection Compatibility Self-Test**: Verify injected payload assumptions after each attach and fail fast with precise diagnostics when Discord updates break module hooks or store lookups.

---

## Credits & Acknowledgements

- Original quest runner logic inspired by [aamiaa's gist](https://gist.github.com/aamiaa/204cd9d42013ded9faf646fae7f89fbb).
- UI built with .NET MAUI.
