# QUEST AUTOMATION

Advanced minimalist suite for Discord automation, providing secure quest rewards claiming and message cleanup protocols.

This project is a high-performance .NET MAUI application that interfaces with the Discord client via the WebSocket Debug protocol. It enables safe, real-time script injection within a distraction-free, black-and-white workspace.

## Features

### Quest Runner
- Full Automation: Handles Game and Streaming missions without manual intervention.
- Secure Claiming: Automatically secures rewards upon successful quest completion.
- CDP Injection: Uses the standard Discord Debug protocol for stable execution.

### Message Deleter
- Granular Purge: Target specific users within any channel for thorough cleanup.
- Responsive Logic: Double-confirmation workflow (Count then Purge) ensures data safety.
- Real-time Stream: Monitoring console provides instant feedback for every neutralized message.
- Emergency Abort: Immediate halt functionality to terminate operations mid-process.

## Interface

### Quest Runner
![Quest Runner Interface](DiscordQuestRunner/questpage.png)

### Message Deleter
![Message Deleter Interface](DiscordQuestRunner/deletionpage.png)

## Guide

1. Launch the application workspace (dotnet run -f net9.0-windows10.0.19041.0) - NOT PUBLISHED YET.
2. Choose your protocol from the main dashboard.
3. For Automated Quests, click "RUN AUTOMATION".
4. For Message Cleanup, input the target IDs and click "START PURGE".
5. Confirm the system's request to interface with Discord via Debug mode if prompted.
6. Monitor the operational stream via the real-time terminal window.

## System Requirements

- Windows 10 or 11 (64-bit)
- Discord Desktop Application (Official Build)

## Technical Stack

- Backend: C# / .NET MAUI
- Logic: JavaScript (Chrome DevTools Protocol execution)
- Interface: XAML / Minimalist Vector Design
- Connection: Local WebSocket Tunnel (Discord Debug Bridge)
