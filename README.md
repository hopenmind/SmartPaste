<div align="center">
  <img src="Assets/logo.svg" alt="SmartPaste Logo" width="128" height="128">
  <h1>SmartPaste</h1>
  <p>A lightweight, invisible Windows utility to transform how you paste, copy, and manage text.</p>

  [![License: All Rights Reserved](https://img.shields.io/badge/License-All%20Rights%20Reserved-red.svg)](LICENSE)
  [![Made by](https://img.shields.io/badge/Made%20by-Hope%20'n%20Mind-blue.svg)](https://www.hopenmind.com)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-lightgrey.svg)](#)
  [![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](#)
</div>

---

## Overview

**SmartPaste** is a stealthy system tray application for Windows that runs silently near your clock, ready to solve everyday friction when dealing with text, lists, equations, and window management. It intercepts custom global keyboard shortcuts and performs intelligent clipboard operations — all without ever stealing focus from your active window.

Whether you're fighting poorly designed web forms that refuse to accept pasted keyword lists, struggling to copy rendered LaTeX equations from Wikipedia into Word without them breaking, or needing to make automated text input look indistinguishable from a real human typing, SmartPaste has you covered.

---

## Features

### Smart Paste — Intelligent List Pasting

Copy a list of keywords, tags, or any text. SmartPaste will type it out for you, character by character, as if a human were doing it.

| Shortcut | Mode | Behavior |
|---|---|---|
| `Ctrl`+`Win`+`V` | **Normal** | Types the entire clipboard content letter by letter at your configured speed. |
| `Ctrl`+`Alt`+`V` | **Word-by-Word + Space** | Splits your text into individual items, types each one, then presses `Space`. Ideal for inline tag fields. |
| `Ctrl`+`Shift`+`V` | **Word-by-Word + Enter** | Splits your text into individual items, types each one, then presses `Enter`. Perfect for forms that validate one keyword at a time. |

**Smart Auto-Splitting Engine:**
SmartPaste doesn't need you to configure delimiters. It automatically detects and splits your copied text on any of the following characters:
`Enter` `Comma` `Semicolon` `Colon` `Dot` `Slash` `Backslash` `Pipe` `Bullet` `Middle Dot` `Tab`
Plus full-width Unicode variants used in CJK text (，；：。、｜). If none of these are found, it falls back to splitting by spaces.

**Human Simulation Mode (Telework Mode):**
Add `Shift` to *any* Smart Paste shortcut (e.g., `Ctrl`+`Shift`+`Win`+`V`) to activate realistic human typing simulation:
- **Variable rhythm** — each keystroke has a randomized delay, mimicking natural typing speed fluctuations.
- **Micro-pauses** — occasional longer pauses (300–800ms) simulate moments of "thinking."
- **Flow bursts** — sudden accelerations where several characters are typed rapidly, simulating "being in the zone."
- **Realistic typos** *(optional)* — rare chance of typing a wrong letter, pressing `Backspace`, and retyping correctly. Makes the input completely indistinguishable from a real person.

This mode is designed to bypass productivity monitoring software ("bossware"), anti-bot form detectors, and any system that flags instant or perfectly rhythmic text input as automated.

---

### Smart Copy — Faithful Web Content Capture

**Shortcut:** `Ctrl`+`Shift`+`C`

Copies selected content from web pages and documents while preserving **exact visual fidelity** — including rendered math equations, SVG graphics, and images.

**How it works:**
1. Intercepts the clipboard's HTML content after a normal copy.
2. Detects all `<img>` tags (PNG, JPEG, GIF, SVG) and inline `<svg>` elements (commonly used by MathJax for equations).
3. Downloads each image in memory and converts it to a **Base64 data URI** — embedding the image directly inside the HTML code.
4. Replaces the original web URLs with these self-contained Base64 blocks.
5. Preserves RTF and plain text fallbacks for compatibility with simpler editors.

**Result:** Paste into Word, LibreOffice, Notion, or email clients and get the **exact same visual layout** you saw on screen — equations render perfectly, images don't break, and text remains editable. No more broken LaTeX or missing images when copying from Wikipedia, arXiv, or scientific journals.

*You no longer have to fight with formats — you copy what you see, you paste what you saw.*

---

### Case Converter — Instant Text Case Cycling

**Shortcut:** `Ctrl`+`Win`+`C`

Select any text in any application, press the shortcut, and it cycles through four case modes automatically:

1. `lowercase` → `UPPERCASE` → `Title Case` → `aLtErNaTiNg CaSe` → back to `lowercase`

Each press advances to the next mode. No menus, no configuration — just select, press, done.

---

### Always On Top — Pin Any Window

**Shortcut:** `Ctrl`+`Alt`+`T`

Click on any window to make it active, press the shortcut, and that window stays pinned above all others. Press the shortcut again to release it.

Useful for keeping a calculator, reference document, video player, or terminal visible while working in other applications.

---

## Settings & Configuration

Right-click the SmartPaste tray icon and select **Settings** to access the full configuration panel. The settings window has three tabs:

### ⌨ Shortcuts Tab
A complete reference table of all keyboard shortcuts, organized by category (Smart Paste, Utilities), with descriptions of each mode's behavior and the smart splitting engine.

### ⚙ Settings Tab
- **Typing Speed Slider** — Adjust the base delay between keystrokes (0ms to 200ms). Default is 30ms.
- **Human Simulation Toggle** — Enable realistic typing rhythm simulation for all paste modes.
- **Realistic Typos Toggle** — When Human Simulation is active, optionally include rare typos with automatic backspace-and-retype behavior.
- **Start Minimized** — Launch SmartPaste directly to the system tray without showing the settings window.
- **Auto-start with Windows** — Copies the application to `%LOCALAPPDATA%\SmartPaste\` and registers it in the Windows startup registry. Unchecking this removes the registry entry.
- **Reset to Defaults** — Restores all settings to their original values.

### ℹ About Tab
Application information, feature overview, developer credits, and license details. Includes a clickable link to [hopenmind.com](https://www.hopenmind.com).

---

## Settings Persistence

All settings are automatically saved to a JSON file at:
```
%LOCALAPPDATA%\SmartPaste\settings.json
```

Your typing speed, simulation preferences, and startup options are preserved between sessions.

---

## Installation

1. Download the latest `.exe` release from the [Releases](#) page.
2. Run the executable. The SmartPaste icon appears in your System Tray (near the clock).
3. Right-click the icon → **Settings** to configure your preferences.

### First Launch Behavior
By default, the settings window opens on first launch so you can review all shortcuts and features. Enable **Start minimized to system tray** in Settings to skip this on future launches.

---

## Building from Source

SmartPaste is built with **C# (.NET 8 WPF)** and uses the Windows Win32 API for global keyboard hooks and clipboard manipulation.

**Requirements:**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 or later

```bash
git clone https://github.com/hopenmind/smartpaste.git
cd smartpaste
dotnet build
dotnet run
```

The compiled executable will be in `bin/Debug/net8.0-windows/SmartPaste.exe`.

---

## Architecture

| Component | Purpose |
|---|---|
| `GlobalHotkey` | Win32 `RegisterHotKey` wrapper for system-wide keyboard interception |
| `SmartPasteManager` | Core paste engine with 3 modes, smart splitting, and human simulation |
| `SmartCopyManager` | HTML clipboard interceptor with Base64 image/SVG embedding |
| `CaseConverterManager` | Text case cycling engine (lower/upper/title/alternating) |
| `AlwaysOnTopManager` | Win32 `SetWindowPos` wrapper for window pinning |
| `AutoStartManager` | AppData self-copy and Windows Registry startup management |
| `SettingsManager` | JSON-based settings persistence in `%LOCALAPPDATA%` |

---

## Security & Privacy

- SmartPaste runs entirely **locally** on your machine. No data is sent over the network except when Smart Copy downloads images from web pages you explicitly copy from.
- The Human Simulation mode uses randomized timing — no two paste sessions produce identical keystroke patterns.
- Global keyboard hooks only listen for the specific registered shortcut combinations. All other keystrokes pass through untouched.

---

## Contributing

Contributions, issues, and feature requests are welcome. Please read our [Contributing Guidelines](CONTRIBUTING.md) before submitting a pull request.

---

## License

**All Rights Reserved.**

This software is provided free of charge for personal, non-commercial use only. No right of exploitation, distribution, modification, or commercial use is granted without a prior written license agreement from Hope 'n Mind.

See the [LICENSE](LICENSE) file for full terms.

---

<div align="center">
  <a href="https://www.hopenmind.com">www.hopenmind.com</a>
</div>
