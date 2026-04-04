<div align="center">
  <img src="Assets/logo.svg" alt="SmartPaste Logo" width="128" height="128">
  <h1>Smart Paste</h1>
  <p>A lightweight, invisible Windows utility to transform how you paste, copy, and manage text.</p>

  [![License: All Rights Reserved](https://img.shields.io/badge/License-All%20Rights%20Reserved-red.svg)](LICENSE)
  [![Made by](https://img.shields.io/badge/Made%20by-Hope%20'n%20Mind-blue.svg)](https://www.hopenmind.com)
</div>

---

## 🚀 About Smart Paste

**Smart Paste** is a stealthy system tray application for Windows designed to solve everyday friction when dealing with text, lists, equations, and window management. Whether you're fighting poorly designed forms that don't accept list pasting, or struggling to copy LaTeX equations from Wikipedia without breaking them, Smart Paste has your back.

Originally conceived as a "Smart Paste" utility to act as a disciplined user typing out lists item by item, it has evolved into a minimalist swiss-army knife for productivity.

## ✨ Features

### 1. Smart Paste Modes (`Ctrl`+`Shift`+`V` / `Ctrl`+`Alt`+`V` / `Ctrl`+`Win`+`V`)
Transform a copied list of keywords into an automated sequence of keystrokes.
- **Normal Mode:** Types the text letter by letter.
- **Word-by-Word + Space:** Types each word progressively, followed by an automatic Space.
- **Word-by-Word + Enter/Comma:** Types each word, followed by an automatic `Enter` (or a chosen separator). *Perfect for populating tag inputs, search fields, or forms that require validation per item!*

### 2. Case Converter (`Ctrl`+`Shift`+`C`)
Instantly change the case of any selected text without retyping it.
- Cycles through: `lowercase` ➔ `UPPERCASE` ➔ `Title Case` ➔ `aLtErNaTiNg CaSe`.

### 3. Always On Top (`Ctrl`+`Alt`+`T`)
Pin any window to stay above all other windows.
- Select a window, press the shortcut, and it stays on top. Press again to unpin.

### 4. Smart Copy (`Ctrl`+`Win`+`C`)
Copies exact visual layouts from web pages—including complex math equations (MathML/MathJax) and images—by embedding them as Base64.
- Say goodbye to broken LaTeX when pasting into Word or Notion!
- Captures the exact fidelity of the original document.

## 🛠️ Installation

1. Download the latest `.exe` release from the [Releases](#) page.
2. Run the executable. It will minimize to your System Tray (near the clock).
3. Right-click the icon to open the **Settings** menu and adjust typing speeds or paste separators.

*Optionally, check the "Run at startup" option (coming soon) to have it always ready!*

## 🧑‍💻 Building from Source

Smart Paste is built with **C# (.NET 8 WPF)** using the Windows Win32 API for low-level global hooks.

```bash
git clone https://github.com/your-username/SmartPaste.git
cd SmartPaste/SmartPaste
dotnet build
dotnet run
```

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!
Feel free to check [issues page](#). If you want to contribute, please read our [Contributing Guidelines](CONTRIBUTING.md).

## 🛡️ Security

Please read our [Security Policy](SECURITY.md) for reporting vulnerabilities.

## 📝 License

**All Rights Reserved.**

This software is provided free of charge for personal use. However, no right of exploitation, distribution, modification, or commercial use is granted without a prior written license agreement. See the [LICENSE](LICENSE) file for more details.

---
*Created by the autists and psychopaths at [Hope 'n Mind](https://www.hopenmind.com).*
