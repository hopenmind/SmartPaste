<div align="center">
  <img src="assets/logo.svg" alt="SmartPaste Logo" width="128" height="128">
  <h1>SmartPaste</h1>
  <p><strong>Windows Clipboard Intelligence Layer</strong></p>
  <p>Copy once, paste perfectly — everywhere. SmartPaste detects your target application<br>and injects the optimal clipboard format automatically.</p>

  [![License: All Rights Reserved](https://img.shields.io/badge/License-All%20Rights%20Reserved-red.svg)](LICENSE)
  [![Made by](https://img.shields.io/badge/Made%20by-Hope%20'n%20Mind-blue.svg)](https://www.hopenmind.com)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6.svg)](#)
  [![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](#)
  [![i18n](https://img.shields.io/badge/i18n-EN%20%7C%20FR%20%7C%20BR-green.svg)](#localization)
</div>

---

## The Problem

You copy a beautifully formatted article from the web — images, equations, diagrams. You paste into Word. What do you get? Broken images, missing formatting, garbled equations. You paste the same thing into Notepad and get HTML tags. Into Paint and get nothing.

**Every application speaks a different clipboard language.** Windows puts multiple formats on the clipboard and hopes the target picks the right one. It usually doesn't.

## The Solution

SmartPaste sits between your copy and your paste. It intercepts Ctrl+V, detects the target application, and **injects only the format that app handles best**:

| Target | Format Injected | Result |
|--------|----------------|--------|
| Word, Excel, Outlook | RTF with `\pict` embedded images | Perfect formatting, images inline |
| Chrome, Firefox, Edge | CF_HTML with local file references | Full fidelity web content |
| VS Code, Slack, Discord | CF_HTML (Chromium renderer) | Rich text preserved |
| Notepad, terminals | Plain text only | Clean, no markup |
| Paint, Photoshop | CF_BITMAP | Image paste |
| Unknown app | All formats | Let the app choose |

The target app has no choice — it receives exactly what it can handle.

---

## Features

### Smart Copy — ContentPackage Engine

**Default shortcut:** `Ctrl+Shift+C` | **Optional:** replaces native `Ctrl+C`

When you copy web content, SmartPaste captures everything into a structured **ContentPackage** — a local cache on disk:

```
%TEMP%\SmartPaste\current\
  manifest.json          metadata + image descriptors
  fragment.html          HTML with file:// image references
  content.rtf            RTF with \pict binary-embedded images
  text.txt               plain text fallback
  selection.png          browser bitmap snapshot
  images/
    img_000.png          downloaded + cached locally
    img_001.jpg
    svg_000.svg
```

**How it works:**
1. Triggers a normal `Ctrl+C` to let the browser populate the clipboard
2. Extracts the HTML fragment and source URL
3. Downloads all `<img>` sources (PNG, JPEG, GIF, WebP, BMP) with size limits (10 MB/image, 50 max)
4. Converts inline `<svg>` elements (MathJax equations, diagrams) to local files
5. Detects MIME types via magic bytes, not file extensions
6. Generates RTF with `\pict\pngblip` binary-embedded images (native Office format)
7. Converts non-standard formats (GIF, WebP, BMP) to PNG via WPF/WIC
8. Saves the browser's bitmap render as `selection.png` (universal fallback)
9. Tags the clipboard with a `SmartPaste_CopyId` so SmartPaste recognizes it later

**No more Base64.** Images are real files, not inflated data URIs. The clipboard stays small. The cache auto-purges after 1 hour.

---

### Smart Paste — Target-Aware Injection

**Default shortcuts:** `Ctrl+Shift+V` / `Ctrl+Alt+V` / `Ctrl+Win+V` | **Optional:** replaces native `Ctrl+V`

#### SmartInject (Ctrl+V override)

When you press `Ctrl+V` and a ContentPackage exists on the clipboard:

1. **PasteInterceptor** (low-level keyboard hook `WH_KEYBOARD_LL`) suppresses the native paste
2. **TargetDetector** identifies the foreground process (`GetForegroundWindow` + `GetWindowThreadProcessId`)
3. **SmartInject** sets ONLY the optimal format on the clipboard
4. Sends a new `Ctrl+V` — the target app receives exactly what it needs

70+ applications are classified across 6 categories:

| Category | Apps | Strategy |
|----------|------|----------|
| **Office** | Word, Excel, PowerPoint, Outlook, OneNote | CF_RTF with `\pict` images |
| **Browser** | Chrome, Firefox, Edge, Opera, Brave, Vivaldi, Arc | CF_HTML with file:// refs |
| **Electron** | VS Code, Slack, Discord, Teams, Notion, Obsidian, Figma | CF_HTML |
| **RichText** | WordPad, LibreOffice, WPS | CF_RTF |
| **PlainText** | Notepad, Notepad++, terminals, Vim | CF_UNICODETEXT |
| **ImageEditor** | Paint, Photoshop, GIMP, Krita, Inkscape | CF_BITMAP |

When SmartCopy content isn't present, `Ctrl+V` passes through untouched. Zero impact on normal use.

#### Typing Simulation Modes

Three paste modes for text-only content, with optional human simulation:

| Mode | Shortcut | Behavior |
|------|----------|----------|
| **Enter** | `Ctrl+Shift+V` | Splits on delimiters, types each segment + Enter |
| **Space** | `Ctrl+Alt+V` | Splits on delimiters, types each segment + Space |
| **Normal** | `Ctrl+Win+V` | Types character by character |

**Smart Auto-Splitting** detects and splits on: newlines, commas, semicolons, colons, dots, slashes, pipes, bullets, and CJK variants. Falls back to spaces if none found.

---

### Telework Mode — Human Typing Simulation

SmartPaste's simulation engine makes automated typing indistinguishable from a real human. Every parameter is tunable.

#### Natural Rhythm
| Option | Effect |
|--------|--------|
| **Variable speed** | Keystroke delays fluctuate randomly around the base value |
| **Thinking pauses** | Brief hesitations (300-800ms) every few words |
| **Flow bursts** | Sudden rapid-fire sequences (10-40ms) for 5-20 characters |
| **Breathing rhythm** | Longer pauses (400-1200ms) at configurable intervals |
| **End-of-line pause** | Short delay after each Enter/newline |

#### Human Mistakes
| Option | Effect |
|--------|--------|
| **Typos** | Wrong key, Backspace, correct key (1.5% probability) |
| **Caps errors** | Wrong capitalization, Backspace, retype (0.8%) |
| **Double-tap** | Same key twice by accident, Backspace (1.0%) |
| **Cursor navigation** | Arrow keys to go back and fix errors |
| **Auto-correct** | Automatically fixes simulated mistakes after a delay |

---

### Command Center — Telework Dashboard

A fullscreen control panel for automated text input. Activated from the Telework settings tab.

#### Manual Mode
Paste or type text, click **START**, switch to the target app during the 3-2-1 countdown, and SmartPaste types it with full human simulation. Live preview shows progress in real-time.

#### Auto Writer Mode
Configure **sources** and **targets** for fully automated operation:

**Sources** (text to type):
- Local files: `.txt`, `.md`, `.log`, `.html`, `.htm`, `.rtf`
- URLs: any `https://` — HTML is automatically stripped to plain text
- Multiple sources rotate in random order for variety

**Targets** (where to type):
- Any `.exe` — launches the app if not running, focuses it if already open
- Multiple targets rotate randomly
- Optional: clear document before typing (`Ctrl+A` + `Delete`)

**Controls:**
- **START** — begins with 3-2-1 countdown
- **PAUSE** — suspends mid-character, resumes exactly where stopped
- **STOP** — cancels immediately
- **EXIT** — restores normal window

#### Scheduler — Weekly Autopilot

An analog clock interface inspired by 80s aesthetics:

- **Dark clock face** with luminous tick marks and monospace hour labels
- **Blue arcs** for work periods (morning + afternoon)
- **Grey arc** for lunch break
- **4 draggable handles** that snap to 15-minute increments
- **Digital readout** below: `09:00-12:00 | 12:00-13:30 | 13:30-17:00`
- **Day toggles** (Mon-Sun) as compact pills

**Energy Curve** — simulates realistic human productivity patterns throughout the day:

| Time | Phase | Typing Speed |
|------|-------|-------------|
| 09:00-10:30 | Morning burst | 0.7x delay (fast, energetic) |
| 10:30-12:00 | Normal pace | 1.0x delay |
| 12:00-13:30 | Lunch break | No activity |
| 13:30-15:00 | Post-lunch dip | 1.4x delay (slow, drowsy) |
| 15:00-16:30 | Afternoon | 1.0x delay |
| 16:30-17:00 | Winding down | 1.3x delay |

The scheduler auto-starts typing when work hours begin and auto-stops for lunch and end of day. Configure it once, and it handles the entire work week autonomously.

---

### Case Converter

**Shortcut:** `Ctrl+Win+C`

Select text, press the shortcut. Each press cycles through:

`lowercase` → `UPPERCASE` → `Title Case` → `aLtErNaTiNg CaSe` → back to `lowercase`

---

### Always On Top

**Shortcut:** `Ctrl+Alt+T`

Pins the active window above all others. Press again to release.

---

## Localization

SmartPaste ships in three languages, switchable live via the `[EN]` button in the header:

| Code | Language | |
|------|----------|---|
| `EN` | English | Default |
| `FR` | Francais | |
| `BR` | Brezhoneg | Breton |

Language files are WPF ResourceDictionaries using `DynamicResource` binding — the switch is instant, no restart needed. Adding a language requires one `.xaml` file in `src/Lang/` and one entry in the cycle array.

---

## Design System

SmartPaste uses a centralized design system (`Theme.xaml`) following systematic design principles:

| Aspect | Implementation |
|--------|---------------|
| **Palette** | Slate greys (10 shades) + Blue brand accent (8 shades) + semantic colors |
| **Typography** | Segoe UI Variable (UI), Cascadia Code (mono) |
| **Spacing** | Scale: 4, 8, 12, 16, 24, 32, 48 |
| **Elevation** | 2-tier shadows (ShadowSm, ShadowMd) |
| **Components** | Card, AccentCard, DangerCard, ShortcutInput, BtnPrimary, BtnSecondary, ModernTab |
| **Corners** | 6px cards, 4px inputs |

Zero hardcoded colors in the UI — everything goes through `{StaticResource}` or `{DynamicResource}`.

---

## Architecture

```
SmartCopy (Ctrl+C / Ctrl+Shift+C)
     |
     v
ContentPackage ─── FormatCache (%TEMP%/SmartPaste/current/)
     |                    |
     |              manifest.json + fragment.html + content.rtf
     |              selection.png + images/*.png
     |
SmartPaste (Ctrl+V / shortcuts)
     |
     v
TargetDetector ── GetForegroundWindow() → process name → category
     |
     v
SmartInject ── set targeted clipboard → Ctrl+V
     |
     v
  [AutoWriterEngine] ── source rotation + target launch + cycle loop
     |
     v
  [WorkScheduler] ── state machine (Working/Lunch/BeforeWork/AfterWork)
                      + energy curve multiplier
```

### Source Files

```
src/
  App.xaml / .cs                    Application entry, manager wiring, interceptor
  MainWindow.xaml / .cs             Settings UI + Command Center dashboard
  Theme.xaml                        Design system
  ScheduleClock.xaml / .cs          Analog clock schedule picker

  ContentPackage.cs                 Clipboard model + FormatCache disk storage
  SmartCopyManager.cs               HTML processing, image download, RTF generation
  SmartPasteManager.cs              SmartInject + typing simulation + dashboard API
  PasteInterceptor.cs               WH_KEYBOARD_LL hook + clipboard monitor
  TargetDetector.cs                 Foreground app classification (70+ apps)
  AutoWriterEngine.cs               Source/target rotation, auto-launch, cycles
  WorkScheduler.cs                  State machine scheduler + energy curve

  CaseConverterManager.cs           Text case cycling
  AlwaysOnTopManager.cs             Window pinning (SetWindowPos)
  AutoStartManager.cs               AppData copy + registry startup
  SettingsManager.cs                JSON persistence + models
  GlobalHotkey.cs                   RegisterHotKey wrapper
  ShortcutParser.cs                 Modifier+Key string parser

  Lang/en.xaml                      English
  Lang/fr.xaml                      Francais
  Lang/br.xaml                      Brezhoneg
```

---

## Building from Source

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) + Windows 10/11

```bash
git clone https://github.com/hopenmind/smartpaste.git
cd smartpaste
dotnet build
dotnet run
```

Output: `bin/Debug/net8.0-windows/SmartPaste.exe`

**NuGet dependencies** (auto-restored):
- `Hardcodet.NotifyIcon.Wpf` — system tray integration
- `InputSimulatorCore` — keystroke simulation

---

## Settings

All configuration persists as JSON in `%LOCALAPPDATA%\SmartPaste\settings.json` — shortcuts, toggles, telework options, Auto Writer sources/targets, scheduler week, language.

---

## Roadmap

### Planned

- **Panic button** — one shortcut kills all automation, hides SmartPaste, restores normal state instantly
- **Anti-idle** — micro mouse movements to prevent Teams/Slack "Away" status and screen lock
- **Mouse simulation** — Bezier curve movements, realistic scroll, random clicks between typing sessions
- **Alt+Tab simulation** — switch between apps at realistic intervals to simulate multitasking
- **Periodic Ctrl+S** — auto-save in target apps at human-realistic intervals
- **Day profiles** — different source/target/rhythm configurations per weekday
- **ContentPackage history** — browse and re-paste previous copies, not just "current"
- **Clipboard preview** — visual preview of the ContentPackage before pasting
- **SVG to PNG** — rasterize SVGs for RTF embedding via Svg.NET
- **Learning mode** — remembers which format works for unknown apps
- **Notification toasts** — subtle feedback on SmartCopy/SmartInject actions
- **Dark mode** — full dark theme (design system already supports it)
- **Portable mode** — settings in app directory instead of AppData
- **Silent installer** — MSI/MSIX for enterprise deployment

### Experimental

- **COM automation** — direct Word/Excel object injection for pixel-perfect formatting
- **Clipboard format designer** — visual editor for custom CF_HTML/CF_RTF templates
- **Network sync** — share ContentPackages across machines via LAN
- **Plugin system** — custom processors for specific content types
- **Mobile companion** — phone-to-PC content transfer via QR/local network

---

## Security & Privacy

- **100% local** — no telemetry, no cloud, no accounts, no network calls except explicit image downloads
- Image downloads enforce: 10 MB limit, 10s timeout, 50 images max, magic byte MIME verification
- The keyboard hook intercepts only `Ctrl+V` (when SmartCopy content exists) and `Ctrl+C` (when override enabled) — all other keystrokes pass through
- Human simulation uses randomized timing — no two sessions produce identical patterns
- Image cache auto-purges after 1 hour

---

## License

**All Rights Reserved.**

This software is provided free of charge for personal, non-commercial use only. No right of exploitation, distribution, modification, or commercial use is granted without a prior written license agreement from Hope 'n Mind.

See the [LICENSE](LICENSE) file for full terms.

---

<div align="center">
  <br>
  <a href="https://www.hopenmind.com"><strong>Hope 'n Mind</strong></a>
  <br>
  <sub>Because the clipboard deserved better.</sub>
</div>
