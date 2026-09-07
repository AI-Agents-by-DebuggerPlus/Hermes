---
name: open-local-artifact
description: "Open a file the agent just created (HTML, PNG, PDF, .excalidraw, etc.) in a Windows app. For HTML use Chrome/Edge with a quoted path (paths with spaces like Trading Analytics break otherwise). REQUIRED same turn after write_file. Triggers: открой, покажи, отобрази визуально в браузере, visualize in browser, да."
version: 0.1.2
metadata:
  hermes:
    tags: [hermes, windows, open, html, browser, artifact, p5js, visualization]
---

# Open local artifact (Windows via WSL)

After you **create** a file the user should see (especially `*.html` p5.js), **open it** — do not stop at “open this path yourself”.

## Prefer write under `/mnt/d/...`

`\\wsl.localhost\...` often hangs. Write under project on `D:` (e.g. `hermes/screenshots/`) or copy there before open.

## HTML → Chrome or Edge (required)

1. Do **not** `Start-Process` the `.html` alone — Windows may show the app picker.
2. **Always quote** the file path — `Trading Analytics` has a space; without quotes Chrome opens `.../Trading/` (empty folder).

```bash
FILE="/mnt/d/Programming/AI_Agents/HermesProjects/Trading Analytics/hermes/screenshots/plan.html"
WIN="$(wslpath -w "$FILE")"
powershell.exe -NoProfile -Command "
  \$p = '$WIN'
  \$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
  \$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  if (Test-Path \$chrome) { Start-Process -FilePath \$chrome -ArgumentList (\`\"\$p\`\") }
  elseif (Test-Path \$edge) { Start-Process -FilePath \$edge -ArgumentList (\`\"\$p\`\") }
  else { Start-Process msedge -ArgumentList (\`\"\$p\`\") }
"
```

Simpler equivalent from PowerShell on Windows:

```powershell
Start-Process -FilePath 'C:\Program Files\Google\Chrome\Application\chrome.exe' -ArgumentList '"D:\...\file.html"'
```

## Other files (PNG, PDF)

```bash
WIN="$(wslpath -w "$FILE")"
powershell.exe -NoProfile -Command "Start-Process -FilePath '$WIN'"
```

## Rules

1. HTML: Chrome/Edge + **quoted** full path.
2. After creating HTML — open in the **same turn**.
3. If open fails, report the Windows path in one line.
4. Not for Density/Futures terminals (Launcher → Testing).
