# StickyNotes++

A modern, fast, and feature-rich desktop sticky notes replacement built with **C# .NET WPF** and **SQLite**.

## Key Features

- **Fluid Sidebar & Appbar**: Snaps to the right edge of the screen, dynamically reserving and releasing workspace area.
- **Glassmorphic Note Cards**: Translucent note cards with smooth hover animations, customizable categories, and dynamic tag badges.
- **Floating Notes**: Drag notes out of the sidebar to create floating widgets that can pin to the desktop wallpaper.
- **Rich Editor & Checklists**: Markdown preview, interactive checklists, single-spaced editing, and Ctrl+V clipboard paste-to-attach.
- **Local AI & OCR**: One-click AI summarization/formatting (via local Ollama) and background OCR on image attachments.
- **Global Hotkeys**: `Win + Alt + Z` to toggle the sidebar, and hotkeys for screenshotting, spotlights, meeting notes, and quick capturing.

## Setup & Run

Requires **.NET 10 SDK**.

1. **Build**:
   ```bash
   dotnet build
   ```
2. **Run**:
   Run the generated binary:
   `bin\Debug\net10.0-windows10.0.19041.0\StickyNotes++.exe`
