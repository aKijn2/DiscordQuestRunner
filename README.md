# Discord Quest Runner [NEXUS]

> !! **Update Notice:** Release **1.3.0** will be coming in less than 3 weeks after fixing some recent problems that have been found.

Welcome to **Discord Quest Runner**! This is a simple, automated Windows tool that helps you accept and complete Discord Quests without the hassle. It works directly with your official Discord desktop app to save you time.

---

## What can it do?

- **Auto-Accept Quests:** Automatically finds and accepts available quests on your account.
- **Auto-Complete:** Spoofs games, streams, or watches videos automatically to complete tasks.
- **Auto-Claim:** Claims your rewards the moment a quest is finished!
- **Auto-Captcha:** Automatically detects and solves basic hCaptcha popups for you.
- **Message Cleanup:** Easily count and delete old messages in your Discord channels.

---

## How to Use (Step-by-Step)

1. **Download & Extract**
   Download the latest `DiscordQuestRunner-Win64.zip` from [Releases](https://github.com/aKijn2/DiscordQuestRunner/releases), and extract the folder to your PC.

2. **Open Discord Desktop**
   Start your official Discord app and sign in. *(Note: browser versions of Discord are not fully supported).*

3. **Go to the Quests Page**
   Inside Discord, click on **User Settings** (the gear icon) -> **Quest Inventory** (or **Gift Inventory/Quests**). Wait a few seconds for the page and available quests to load. 
   *(!! **Crucial Step:** The app cannot see your quests until you open this page!)*

4. **Run the App**
   Open `DiscordQuestRunner.exe` and click the **INITIALIZE QUESTS** button.

5. **Sit Back and Relax!**
   If **Auto Accept** is enabled, the tool will enroll in all quests and immediately start completing them. Check the progress in the app's log window!

6. **Don't touch anything!**
   While the bot is clicking Captchas, please let it finish! If Discord is minimized, no worries, it will pop back up to click it!

---

## Common Questions & Troubleshooting

**Q: The app says `No new valid quests available to accept.` or `No uncompleted quests found.`**
**A:** This happens if Discord hasn't loaded your quests into memory yet. Go to the **Quests** page in Discord, wait 5 seconds, and click "INITIALIZE QUESTS" again. Also, keep in mind some accounts simply don't have active quests available.

**Q: A quest stopped progressing midway through!**
**A:** This usually happens if Discord was closed or interrupted. Just restart Discord, open the Quests page again, and rerun the tool. It will pick up where it left off.

**Q: What about Captchas?**
**A:** The tool will attempt to auto-click and solve standard Captcha popups that appear during claiming. If Discord is minimized, the tool will attempt to bring it to the foreground automatically so it can correctly click the Captcha. Give it a few seconds to work its magic.

---

## Are you a Developer?
Want to build from source, see the roadmap, or understand how this works under the hood? Check out our [Developer Guide (DEVELOPMENT.md)](DEVELOPMENT.md).

---

## Disclaimer
This application interacts with the Discord desktop client through its internal debug interfaces. Use it responsibly and at your own risk. Ensure your usage complies with Discord's Terms of Service.