***

# ⚡ Discord Quest Runner [NEXUS]

> A premium, high-performance command center for Discord automation. Securely claim quest rewards and execute precise message cleanup protocols via a dedicated local WebSocket tunnel.

Built on **.NET MAUI 9.0**, this application interfaces directly with the official Discord desktop client using the Chrome DevTools Protocol (CDP). It provides safe, real-time script injection within a sleek, distraction-free workspace.

---

## 🚀 Core Features

### 🎮 Quest Automator
* **Hands-Free Execution:** Fully automates Game and Streaming missions without requiring manual user intervention.
* **Secure Claiming:** Automatically secures and redeems rewards the moment a quest completion sequence is validated.
* **CDP Injection:** Utilizes Discord's native Debug protocol for stable, seamless, and untraceable execution.

### 🗑️ Message Purge Protocol
* **Granular Targeting:** Isolate and target specific user IDs within any channel for thorough, precise cleanup.
* **Failsafe Logic:** Features a strict double-confirmation workflow (Analyze & Count -> Confirm -> Purge) to ensure data safety.
* **Real-Time Telemetry:** The built-in terminal provides instant, line-by-line feedback for every neutralized message.
* **Emergency Abort:** Immediate halt functionality allows the user to terminate the deletion sequence mid-process.

---

## ⚙️ System Requirements

* **OS:** Windows 10 or Windows 11 (64-bit architecture)
* **Client:** Official Discord Desktop Application (PTB or Canary also supported)
* **Framework:** .NET 9.0 SDK (for compilation)

---

## 🛠️ Getting Started

Currently, the application must be compiled and run locally.

**1. Clone the repository and navigate to the project folder:**
```bash
git clone https://github.com/yourusername/DiscordQuestRunner.git
cd DiscordQuestRunner
```

**2. Launch the application workspace:**
```bash
dotnet run -f net9.0-windows10.0.19041.0
```

**3. Operational Guide:**
* Select your desired protocol (Quest Runner or Message Purge) from the main dashboard.
* **For Quests:** Click `INITIALIZE QUESTS`.
* **For Purge:** Input the target `CHANNEL_ID` and `USER_ID`, then click `START PURGE`.
* If prompted by the Nexus system alert, authorize the restart of Discord in Debug Mode.
* Monitor the operation telemetry via the real-time terminal window.

---

## 💻 Technical Stack

* **Architecture:** C# / .NET MAUI 9
* **Execution Logic:** JavaScript (CDP Payload)
* **Interface:** XAML (Custom Nexus UI / Float-Card Design)
* **Bridge:** Local WebSocket Tunnel (`ws://127.0.0.1:9222`)

---

## 🗺️ Roadmap / Known Issues

- [ ] **Codebase Refactor:** Clean up and modularize service injections.
- [ ] **Documentation:** Add XML comments to complex bridging and CDP handshake logic.
- [ ] **Persistence:** Add local storage for target IDs to prevent repetitive typing.

---

## 📜 Credits

* Original quest runner script logic inspired by [aamiaa's gist](https://gist.github.com/aamiaa/204cd9d42013ded9faf646fae7f89fbb).


---
*Disclaimer: This application interacts with the Discord client via debug ports. Use responsibly and in accordance with Discord's Terms of Service.*
