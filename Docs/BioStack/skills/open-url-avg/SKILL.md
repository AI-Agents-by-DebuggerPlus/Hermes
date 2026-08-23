---
name: open-url-avg
description: >
  REQUIRED for opening any http(s) link for the user. Triggers: открой, открой ссылку,
  открой в браузере, open link, open URL, open in browser, iHerb, Amazon, Cloudflare.
  Steps: skill_view(open-url-avg) then run open.sh. Opens URL in AVG Secure Browser
  (AVGBrowser.exe). NEVER use browser_navigate / browser / Playwright — they hit
  Cloudflare on shops. NEVER chrome.exe / msedge.exe for this project workflow.
version: 1.0.0
metadata:
  hermes:
    tags: [hermes, windows, avg, browser, url, open, открой, iherb, amazon, cloudflare]
    related_skills: [open-local-artifact]
    requires_toolsets: [terminal]
---

# Open URL in AVG Secure Browser

## When to Use

Any time the user asks to **open a link / URL / page in the browser**, especially iHerb.

## Hard ban

- Do **not** call `browser_navigate`, `browser_open`, or tool `browser`.
- Do **not** use Chrome/Edge paths — use **AVG** only (this skill).

## Procedure (same turn)

1. `skill_view("open-url-avg")` if you need the full steps (optional if you already know them).
2. For **each** URL, run via **terminal**:

```bash
bash ~/.hermes/skills/domain/open-url-avg/open.sh 'https://example.com/path'
```

3. Chat reply: one short line that it opened in AVG + the same clickable `https://…`.

## Script details

- Binary: `C:\Program Files\AVG\Browser\Application\AVGBrowser.exe`
- Fallback: if AVG missing, `Start-Process` URL (Windows default browser).

## Install (WSL)

Copy this folder to `~/.hermes/skills/domain/open-url-avg/`, `chmod +x open.sh`.

Project mirror: `HermesProjects/BioStack/hermes/skills/open-url-avg/`.

## Verification

Terminal prints `ok avg` (or `ok default-fallback`).

**Verified 2026-08-22:** BioStack New CLI Session — link in chat + open in AVG.
