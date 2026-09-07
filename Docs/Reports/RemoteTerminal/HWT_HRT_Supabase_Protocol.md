# HWT ↔ HRT protocol (Supabase only)

**Version:** 1.0  
**Date:** 2026-08-13  
**Status:** canonical for desktop HRT and a future mobile HRT

HWT and HRT **never** talk via a local file, IPC, or a private socket.  
The only shared bus is **Supabase** (`public.messages` + Storage `chat-files`).

`status.json` / `command.json` exist only **inside the HWT PC** (Hermes.Wpf ↔ HWT). They are not part of this protocol.

---

## 1. Actors

| Actor | Role | Talks to Supabase? |
|-------|------|--------------------|
| **HWT** (`HermesWpfTerminal`) | MT5 UI + local IPC | **No** |
| **Publisher** (`Hermes.Wpf`, project `Mt5Terminal`) | Reads HWT IPC, INSERT/upload | **Yes** |
| **HRT** (desktop or mobile) | View-only consumer | **Yes** |
| **Commander** (AndroidChat / mobile HRT / any client) | Asks for refresh / screenshot / repeat | **Yes** |

```
Commander ──INSERT cmd──► public.messages
                              │  recipient = Hermes.Mt5Terminal
                              ▼
                         Hermes.Wpf
                              │  local IPC only
                              ▼
                            HWT
                              │  snapshot / PNG
                              ▼
                         Hermes.Wpf
                              │  INSERT + Storage
                              ▼
                         public.messages  (recipient = RemoteTerminal)
                              │
                              ▼
                    HRT desktop / HRT mobile
```

---

## 2. Transport

### 2.1 Auth

| Item | Value |
|------|--------|
| Key | Supabase **anon** (not service_role) |
| Mode | Anonymous Auth (`POST /auth/v1/signup` with `{"data":{}}` or `grant_type=anonymous`) |
| INSERT | `sender_id` = `auth.uid()` of the session |

Without anonymous session, INSERT/Storage fail; SELECT may still work with the anon JWT.

### 2.2 Table `public.messages`

| Column | Type | Notes |
|--------|------|--------|
| `id` | uuid | Server PK. Do not send on INSERT. |
| `sender_id` | uuid | Must equal `auth.uid()` |
| `sender_name` | text | Logical name (not auth user) |
| `recipient_name` | text | Routing key |
| `content` | text | Entire payload (JSON or chat text). **No `type` column.** |
| `created_at` | timestamptz | Client ISO-8601 or `now()` |

There is no extra `type` / `meta` column. Discriminate by JSON `content.type`.

### 2.3 Names

| Direction | `sender_name` | `recipient_name` |
|-----------|---------------|------------------|
| HWT state / shot → HRT | `Hermes` | `RemoteTerminal` |
| Commander → HWT (via WPF) | `AndroidChat` or `HrtMobile` | `Hermes.Mt5Terminal` |
| HRT self-logs (optional) | `RemoteTerminal` | `RemoteTerminal` |

HRT **must** filter `recipient_name = RemoteTerminal` (case-insensitive).  
Ignore other recipients (chat, logs to other apps).

### 2.4 Live channel

**Primary:** Phoenix Realtime WebSocket.

```
wss://<project>.supabase.co/realtime/v1/websocket?apikey=<anon>&vsn=1.0.0
topic: realtime:public:messages
event: INSERT
```

Join payload (same as desktop HRT): `postgres_changes` on `public.messages`, event `INSERT`.

On `postgres_changes` → `payload.data.record` is the row.

**Backup (optional, desktop):** REST `GET /rest/v1/messages?select=id,sender_name,recipient_name,content,created_at&order=created_at.desc&limit=40` — only when WS is down **and** the user enabled REST backup. Not a silent poll-fallback.

**History (on demand):** same SELECT, no timer. Mobile: pull-to-refresh is OK; do not poll while WS is connected.

### 2.5 Storage

Bucket: **`chat-files`** (public read if RLS allows; else authenticated GET).

```
GET {supabase}/storage/v1/object/authenticated/{bucket}/{path}
Authorization: Bearer <access_token>
apikey: <anon>
```

Fallback:

```
GET {supabase}/storage/v1/object/public/{path-url-encoded}
```

Path is stored as-is in `hwt_screenshot.path` (includes `{uid}/hwt-screenshots/...`).  
URL-encode each path segment; do not encode `/`.

---

## 3. Messages HWT → HRT

All of these are INSERT into `messages` with `recipient_name=RemoteTerminal`.  
`content` is a single JSON object.

### 3.1 `hwt_status` — account / ticker / positions

Published by Hermes.Wpf **only on command `refresh`** (IPC `snapshot` + publish). No timer.

```json
{
  "type": "hwt_status",
  "utc": "2026-08-13T01:58:58.6435821Z",
  "note": "ok",
  "build": "v41 / HermesWpfTerminalUi41.dll",
  "symbol": "USDJPY",
  "bid": "159.366",
  "ask": "159.366",
  "lot": "0.10",
  "account": "Balance: 100003.74   Equity: 100003.74   Margin: 0.00   Free: 100003.74   Profit: 0.00   USD",
  "market_status": "Market: OPEN | Session: Sydney/Tokyo | …",
  "positions_header": "0 open",
  "real_trading": false,
  "auto_trade": false,
  "positions": [],
  "pending_orders": []
}
```

| Field | Type | Required | UI |
|-------|------|----------|-----|
| `type` | `"hwt_status"` | yes | discriminator |
| `symbol` | string | yes* | ticker |
| `bid` / `ask` / `lot` | string | yes* | prices |
| `account` | string | yes* | account panel (`"   "` / `" | "` → line breaks) |
| `market_status` | string | no | session line |
| `real_trading` / `auto_trade` | bool | no | flags |
| `positions` | string[] | no | open positions (one line each) |
| `pending_orders` | string[] | no | pending; if absent, treat lines containing `pending` / `buy limit` / `sell stop` / `отлож` as pending |
| `positions_header` | string | no | e.g. `0 open` |
| `utc` / `build` / `note` | string | no | debug |

\*Treat as present if any of `symbol`, `bid`, `account`, `real_trading` exist.

**Mobile HRT:** replace the whole dashboard from this object. Do not merge with a previous snapshot.

### 3.2 `hwt_screenshot` — chart image

Published after a successful HWT screenshot IPC.

Preferred (Storage):

```json
{
  "type": "hwt_screenshot",
  "name": "hermes_chart_1786352553_56970265.png",
  "bucket": "chat-files",
  "path": "<uid>/hwt-screenshots/<guid>_hermes_chart_….png",
  "mime": "image/png",
  "size": 184320,
  "nonce": "7bef9d6f97e345a89ac0e156eb9c5bac"
}
```

Fallback (inline, only if Storage failed and size ≤ ~1.2 MB):

```json
{
  "type": "hwt_screenshot",
  "name": "hermes_chart.png",
  "mime": "image/png",
  "size": 90000,
  "data_base64": "<png bytes>",
  "nonce": "…"
}
```

| Field | Notes |
|-------|--------|
| `nonce` | Unique per publish. Dedup key. Always restart viewer on new nonce. |
| `path` + `bucket` | Download PNG from Storage. |
| `data_base64` | Use if `path` missing. |

**Desktop HRT UX (must match on mobile unless noted):**

1. Show image **fullscreen** immediately.
2. Keep visible **10 seconds**.
3. Then blank / dismiss (desktop also sends `SC_MONITORPOWER` — **mobile: just close overlay**).
4. Optional: wake on tap / volume (desktop: Space).

Ignore duplicate `nonce` (except see §3.3).

Also accept legacy `{"type":"file","mime":"image/…","bucket","path","name"}`.

### 3.3 `hwt_screenshot_repeat` — show last shot again

No new upload. HRT must **not** wait for a second `hwt_screenshot`.

```json
{
  "type": "hwt_screenshot_repeat",
  "name": "hermes_chart_….png",
  "nonce": "<new nonce>",
  "replay": true
}
```

**Behaviour:** if a cached image exists → restart the 10 s show.  
If no cache → show error «нужен новый Screenshot»; do not download.

### 3.4 Logs (optional)

`content` starting with `[LOG:HermesWpf]` / `[LOG:RemoteTerminal]` / `[LOG:DesktopVoiceChat]`.  
Not trading UI. Mobile may hide them.

---

## 4. Commands HRT / mobile → HWT

HRT does **not** write IPC. To move HWT, INSERT a **chat command** that Hermes.Wpf already routes.

| Field | Value |
|-------|--------|
| `recipient_name` | `Hermes.Mt5Terminal` |
| `sender_name` | `HrtMobile` (or `AndroidChat` if going through the phone app) |
| `sender_id` | session uid |
| `content` | see below |

Hermes.Wpf must be running and subscribed (Realtime). Project folder name = `Mt5Terminal`.

### 4.1 Natural language (current production)

| User text | Effect |
|-----------|--------|
| `refresh` / `обновить` | IPC snapshot + publish `hwt_status` |
| `screenshot` / `скрин` / `screeshot` (typo OK) | IPC screenshot + publish `hwt_screenshot` |
| `повтор` / `repeat` | local repeat → `hwt_screenshot_repeat` only (no CLI) |

### 4.2 Whitelist JSON (agent stdout / optional client)

```json
{"action":"refresh","id":"<uuid without dashes>"}
{"action":"screenshot","id":"<uuid>"}
{"action":"snapshot","id":"<uuid>"}
```

`refresh` is remapped to IPC `snapshot` then Supabase publish.  
`id` = 32 hex chars recommended.

Other whitelist actions (`buy`, `sell`, `close`, …) are **trading**, not HRT. Mobile HRT should not send them unless it is a trading client.

After a command, wait for the matching `hwt_status` / `hwt_screenshot` / `hwt_screenshot_repeat` on `recipient_name=RemoteTerminal`. Typical latency: 1–10 s (screenshot up to ~60 s).

---

## 5. Dedup and races

| Rule | Why |
|------|-----|
| Dedup feed by `messages.id` | WS + REST backup + F5 |
| Dedup shots by `nonce` (else `path`) | Re-uploads / retries |
| Repeat = **only** `hwt_screenshot_repeat` | A second `hwt_screenshot` ~1 s later races with the 10 s blank |
| First REST SELECT is baseline (do not dump history) | Same as desktop / Xp |
| First on-demand poll may return only the newest screenshot-like row | Desktop Poll/F5 |

---

## 6. Minimal mobile HRT checklist

1. Anonymous auth + keep JWT fresh.
2. Subscribe Realtime INSERT on `public.messages`.
3. Keep rows with `recipient_name=RemoteTerminal`.
4. Parse `content` JSON `type`:
   - `hwt_status` → dashboard
   - `hwt_screenshot` → download + 10 s fullscreen
   - `hwt_screenshot_repeat` → 10 s from cache
5. To refresh: INSERT to `Hermes.Mt5Terminal` with `refresh`.
6. To shot: INSERT `screenshot`.
7. To replay: INSERT `повтор`.
8. Do **not** read any `status.json` / local path.
9. Do **not** poll REST while WebSocket is connected.

---

## 7. Out of protocol

| Thing | Where it belongs |
|-------|------------------|
| `hermes/ipc/status.json` | HWT ↔ Hermes.Wpf on the MT5 PC only |
| `command.json` / `result.json` | same |
| HWT local fullscreen viewer | HWT UI, not HRT |
| AndroidChat TTS `[Voice]` | chat to phone, not HRT |

---

## 8. Code map (desktop reference)

| Piece | Path |
|-------|------|
| Publish status | `Hermes.Wpf/Services/HwtStatusSupabasePublisher.cs` |
| Publish screenshot | `Hermes.Wpf/Services/SupabaseChatRelayService.cs` → `PublishHwtScreenshotAsync` |
| Router refresh/screenshot | `Hermes.Wpf/Services/Mt5TerminalTradeRouter.cs` |
| HRT ingest | `Hermes.RemoteTerminal/MainWindow.xaml.cs` |
| HRT Realtime | `Hermes.RemoteTerminal/Services/SupabaseRealtimeMessages.cs` |
| HRT REST backup | `Hermes.RemoteTerminal/Services/SupabaseRestBackupBridge.cs` |

---

## 9. Compatibility

- **proto 1.0** = message types and fields in this file.
- New fields: additive, ignore unknown keys.
- New `type` values: ignore (do not crash).
- Breaking change: bump this file to 1.1+ and note in `Docs/Reports/RemoteTerminal/README.md`.
