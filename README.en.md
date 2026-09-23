# Codex Limit Viewer

[日本語](README.md)

A sleek, lightweight Windows 11 system tray application that continuously displays remaining quota for two selected services in a two-line layout. Codex and Antigravity CLI (`agy`) are selected by default.

![Taskbar Preview](docs/taskbar.png)

## Features

- **Always Visible**: Glance at your remaining quota percentage (lowest bucket) directly in your Windows 11 notification area without interrupting your work.
- **Choose Two Rows**: Select Codex, Antigravity, or Claude Code for the top and bottom rows from the right-click menu. Selecting an item already in the other row automatically swaps them.
- **Details on Hover**: Off by default. When enabled, hovering displays per-bucket quota, reset countdowns (days, hours, minutes), reset dates for periods crossing into later days, and the last updated timestamp in a smooth fade popup. It fades out as soon as the pointer leaves both the display and popup. When disabled, a centered tooltip shows the app name.
- **Multi-Service Integration**: Integrates with Codex (prioritizing the desktop app's bundled binary or standalone Codex CLI) and Antigravity (`agy`), polling every 60 seconds without extracting or storing credentials. Receives Claude Code usage directly from its official status line.
- **Graceful Error Handling**: If a fetch fails, previous values remain visible in gray text rather than disappearing.
- **Bilingual Interface**: Japanese by default, switchable to English anytime via the right-click menu.

![Sample Preview](docs/preview-en.png)

## Getting Started

1. **Prerequisites**:
   - **Codex**: Install and sign in to Codex in the [ChatGPT desktop app](https://learn.chatgpt.com/docs/windows/windows-app), or install the standalone [Codex CLI](https://github.com/openai/codex). Codex requires a local `codex.exe`.
   - **Antigravity**: To display Antigravity quota, install the [Antigravity CLI (`agy`)](https://antigravity.google/docs/cli-install).
   - **Claude Code**: To display Claude Code quota, you need an account with access to Claude Code.
   *Note: This application does not bundle CLI tools.*
2. **Download**:
   Download and extract the latest release ZIP from [Releases](https://github.com/hinatamaxxx/codex-limit-viewer/releases).
3. **Install**:
   Open PowerShell in the extracted directory and run:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ./Install.ps1
   ```
   - Install path: `%LOCALAPPDATA%/Programs/CodexLimitViewer`
   - Data directory: `%LOCALAPPDATA%/CodexLimitViewer`
4. **Taskbar Settings**:
   Open Windows **Settings > Personalization > Taskbar > Other system tray icons**, turn on the application entries, and keep the 4 slots adjacent.

## Usage

![Codex Limit Viewer](docs/menu-en.png)

- **Hover**: Opens the details popup (disabled by default). It fades out immediately after the pointer leaves both the tray display and the popup. When disabled, a centered tooltip shows the app name.
- **Left-Click**: Toggles the details popup. A popup opened by click closes immediately upon another click or clicking outside.
- **Right-Click**: Context menu (hover setting, Refresh Now, Language, Launch at Startup, Exit). Changing displayed items or toggles keeps the menu open; click outside to dismiss it.
- **Displayed Services**: In the right-click menu, hover over "Taskbar display" then "Top row" or "Bottom row" to reveal Codex, Antigravity, and Claude Code. Click only the final choice. The taskbar stays at two rows.
- **Startup**: Opt-in via the right-click menu (disabled by default).

## Claude Code Integration

The app reads 5-hour and 7-day usage percentages from the [official Claude Code status line](https://code.claude.com/docs/en/statusline). If you can use Claude Code, run this once from the extracted folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./EnableClaudeCode.ps1
```

The setup keeps any existing status line unchanged (add the bridge command manually). Otherwise, it backs up your settings and saves only usage percentages and a timestamp locally. After a Claude Code response supplies quota data, it appears in the details window and in one of the two taskbar rows if selected under Taskbar display. Missing data appears as “—”, never as 0%. Availability of rate-limit data depends on your account and Claude Code setup. Installing Claude Desktop alone does not provide Claude Code quota data.

To add another provider, give this repository URL to a coding assistant and ask it to implement support.

## Building from Source (Optional)

Build a self-contained binary (no external runtime installation required) using the .NET 10 SDK:

```bash
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true -o dist
```

## Uninstalling

1. Disable startup from the context menu and exit the application.
2. Remove the installation folder, data folder, and Start Menu shortcut:
   - `%LOCALAPPDATA%/Programs/CodexLimitViewer`
   - `%LOCALAPPDATA%/CodexLimitViewer`

## Notes

- Unofficial application.
- Unsigned pre-release software tested on Windows 11 at 150% DPI. Mixed-DPI configurations and reboot startup behavior are unverified.
- License: [MIT License](LICENSE)
