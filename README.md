# Discord Quest Runner [NEXUS]

> A Windows tool for automating supported Discord quest flows and message cleanup through the Discord desktop client.

Built with **.NET MAUI 9**, this app connects to Discord through the Chrome DevTools Protocol (CDP) and injects JavaScript payloads into the running desktop client.

---

## What it does

Discord Quest Runner can:

- auto-accept visible quests when the option is enabled
- run supported quest types after they are enrolled
- claim rewards after quest completion
- provide a separate message deletion workflow

---

## Quick Start

1. **Open Discord Desktop**  
   Start the official Discord desktop app and sign in to the account you want to use.

2. **Open the Quests page manually**  
   Go to Discord's **Quests** section and wait a few seconds for the quest list to load.

3. **Launch the app**  
   Open `DiscordQuestRunner.exe`.

4. **Run the tool**  
   Click **INITIALIZE QUESTS**.

5. **Let auto-accept run first**  
   If **Auto Accept** is enabled, the app will try to enroll in all valid visible quests for the current account.

6. **Let the runner continue**  
   After enrollment, the runner looks for enrolled, incomplete quests and starts supported flows.

---

## Important usage notes

- **Open the Quests page before every run.** This is required for consistent results.
- **Use the Discord desktop app.** Some quest types do not work correctly in the browser.
- **Quest availability is account-dependent.** One account may have valid quests while another has none.
- **Captcha is still manual.** If Discord asks for a captcha, you must solve it yourself.

---

## Why opening Quests matters

Discord does not always populate its internal quest store until the **Quests** page has been opened in the client.

If you run the tool before opening that page, the app may report:

- `No new valid quests available to accept.`
- `No uncompleted quests found.`

If that happens:

1. open the **Quests** page in Discord
2. wait a few seconds
3. run the tool again

---

## Expected log output

A healthy run usually includes lines like:

- `Connection established with Discord.`
- `Attached to target: Discord` or `Attached to target: Amigos/Friends`
- `AUTO: [DQR] Loaded script asset: auto_accept_v2.js`
- `SCRIPT: [DQR] Loaded script asset: quest_runner_v2.js`

---

## Common outcomes

### `No new valid quests available to accept.`

Usually means there were no enrollable quests for that account or session.

### `No uncompleted quests found.`

Usually means Discord did not expose any active enrolled quests in its in-memory store.

Try opening the **Quests** page manually and rerunning the tool.

### A quest had progress before, but no longer continues

This can happen if Discord was closed during progression.

Try this:

1. restart Discord
2. open the **Quests** page
3. confirm the quest still appears as active in Discord
4. run the tool again

---

## Features

### Quest automation

- **Auto-accept support**: Enrolls in valid visible quests when enabled.
- **Quest runner**: Handles supported quest flows after Discord has loaded quest data.
- **Reward claiming**: Attempts to claim rewards after completion.
- **CDP bridge**: Uses Discord's debug interface for script execution.

### Message deletion

- **Targeted cleanup**: Delete messages for a selected user in a channel.
- **Safer workflow**: Analyze → count → confirm → delete.
- **Live log output**: See progress in real time.
- **Abort support**: Stop a deletion run immediately.

---

## Installation

### Option 1: Download a release

1. Download the latest `DiscordQuestRunner-Win64.zip` from [Releases](https://github.com/aKijn2/DiscordQuestRunner/releases).
2. Extract it.
3. Run `DiscordQuestRunner.exe`.

### Option 2: Build from source

```bash
git clone https://github.com/aKijn2/DiscordQuestRunner.git
cd DiscordQuestRunner
dotnet run -f net9.0-windows10.0.19041.0
```

---

## Technical stack

- **App**: C# / .NET MAUI 9
- **UI**: XAML
- **Runtime scripts**: JavaScript
- **Bridge**: CDP over local debug port (`127.0.0.1:9222`)

---

## Roadmap

- [x] **Auto-Accept Quests**: Automatically accept currently valid visible quests.
- [ ] **Captcha Handling**: Improve workflow around manual captcha interruptions.
- [ ] **Persistence**: Save message purge targets locally.
- [ ] **Refactor**: Clean up and modularize service code.
- [ ] **Documentation**: Expand internal code comments and technical docs.

---

## Credits

- Original quest runner logic inspired by [aamiaa's gist](https://gist.github.com/aamiaa/204cd9d42013ded9faf646fae7f89fbb).

---

## Disclaimer

This application interacts with the Discord desktop client through its debug interface. Use it responsibly and at your own risk, and make sure your usage complies with Discord's Terms of Service.

---