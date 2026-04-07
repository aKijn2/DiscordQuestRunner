<div align="center">
  <h1>Discord Quest Runner [NEXUS]</h1>
  <p><strong>Your all-in-one Discord automation assistant!</strong></p>
  <a href="https://github.com/aKijn2/DiscordQuestRunner/releases/tag/v1.3.0">
    <img src="https://img.shields.io/github/v/release/aKijn2/DiscordQuestRunner?style=for-the-badge&color=success" alt="Latest Release" />
  </a>
  <img src="https://img.shields.io/badge/Platform-Windows-0078d7?style=for-the-badge&logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/Made_with-.NET_MAUI_9-512bd4?style=for-the-badge&logo=dotnet" alt=".NET MAUI" />
</div>

<br>

> **Release 1.3.0 is out!** Includes major bug fixes for the "Watch Video" quests freezing and improves Auto-Captcha clicking even when Discord is minimized.

Welcome to **Discord Quest Runner**! This is a simple, automated Windows tool that securely connects to your official Discord desktop app to accept and complete Discord Quests without the hassle. It saves you time by doing the heavy lifting in the background.

---

## Featured Capabilities

- **Auto-Accept Quests:** Automatically finds and accepts available quests on your account.
- **Auto-Complete:** Spoofs games, streams, or watches videos automatically to complete tasks.
- **Auto-Claim:** Claims your rewards the moment a quest is finished!
- **Auto-Captcha:** Automatically detects and solves basic hCaptcha popups for you.
- **Message Cleanup:** Easily count and delete old messages in your Discord channels.

---

## How to Use (Step-by-Step)

**1. Download & Extract**
Download the latest `DiscordQuestRunner-Win64.zip` from [Releases](https://github.com/aKijn2/DiscordQuestRunner/releases), and extract the folder to your PC.

**2. Open Discord Desktop**
Start your official Discord app and sign in. *(Note: browser versions of Discord are not fully supported).*

**3. Go to the Quests Page**
Inside Discord, click on **User Settings** (the gear icon) -> **Quest Inventory** (or **Gift Inventory**). Wait a few seconds for the page and available quests to load. 
> *Crucial Step: The app cannot see your quests until you open this page!*

**4. Run the App**
Open `DiscordQuestRunner.exe` and click the **INITIALIZE QUESTS** button.

**5. Sit Back and Relax!**
If **Auto Accept** is enabled, the tool will enroll in all quests and immediately start completing them. Check the progress in the app's log window.

**6. Don't touch anything!**
While the bot is clicking Captchas, please let it finish! If Discord is minimized, no worries, it will pop back up to click it for you.

---

## Common Questions & Troubleshooting

<details>
<summary><strong>The app says "No new valid quests" or "No uncompleted quests found"</strong></summary>
<br>
This happens if Discord hasn't loaded your quests into memory yet. Go to the <strong>Quests</strong> page in Discord, wait 5 seconds, and click "INITIALIZE QUESTS" again. Keep in mind some accounts simply don't have active quests available.
</details>

<details>
<summary><strong>A quest stopped progressing midway through!</strong></summary>
<br>
This usually happens if Discord was closed or interrupted. Just restart Discord, open the Quests page again, and rerun the tool. It will pick up where it left off.
</details>

<details>
<summary><strong>What about Captchas?</strong></summary>
<br>
The tool will attempt to auto-click and solve standard Captcha popups that appear during claiming. If Discord is minimized, the tool will attempt to bring it to the foreground automatically so it can correctly click the Captcha. Give it a few seconds to work its magic.
</details>

---

## Are you a Developer?
Want to build from source, see the roadmap, or understand how this works under the hood? Check out our [Developer Guide (DEVELOPMENT.md)](DEVELOPMENT.md).

---

<p align="center">
  <small>
    <b>Disclaimer</b><br>
    This application interacts with the Discord desktop client through its internal debug interfaces. Use it responsibly and at your own risk. Ensure your usage complies with Discord's Terms of Service.
  </small>
</p>