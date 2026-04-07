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

## Roadmap

- [x] **Auto-Accept Quests**: Automatically accept currently valid visible quests.
- [x] **STATS ROW**: Implement stats row logic.
- [x] **Captcha Handling**: Improve workflow around manual/automated captcha interruptions.
- [x] **Fix Watch Video**: Progress logic refactored to allow unhindered loop and robust CDP payload synchronization.
- [ ] **Multi-Account Support**: Manage and run quests across multiple Discord instances/profiles smoothly.
- [ ] **Custom Settings UI**: Allow users to configure clicker delays and interval settings directly from the app interface.
- [ ] **Headless/CLI Mode**: Support running the runner from the command line without the GUI for power users.
- [ ] **Linux Support**: Port the process detection and path resolution to support Linux Discord clients (Native/Flatpak/Snap).
- [ ] **Refactor**: Clean up and modularize service code.

---

## Credits & Acknowledgements

- Original quest runner logic inspired by [aamiaa's gist](https://gist.github.com/aamiaa/204cd9d42013ded9faf646fae7f89fbb).
- UI built with .NET MAUI.