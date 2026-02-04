# Cliparino “Just Works” Rewrite Plan (Tray App, Modern C#)

Owner: Scott  
Repo: Cliparino (angrmgmt/Cliparino) :contentReference[oaicite:0]{index=0}  
Primary goal: Remove dependency on Streamer.bot while improving reliability and minimizing end-user setup.

---

**NOTE:
Functional and dependency parity is defined exclusively by PARITY_CHECKLIST.md.
PLAN.MD describes intent and sequencing; it is not authoritative for parity.**

## 1) Source of Truth: Existing Requirements (Repo-Defined)

This plan is explicitly anchored to the current repo’s functional requirements as expressed in the README:

### 1.1 Feature requirements (current state)
- Play specific Twitch clips :contentReference[oaicite:1]{index=1}
- Enqueue clips for playback :contentReference[oaicite:2]{index=2}
- Play clips that users post in chat :contentReference[oaicite:3]{index=3}
- Shoutout: play random clip from a channel (and send shoutout message + `/shoutout`) :contentReference[oaicite:4]{index=4}
- Stop clip playback :contentReference[oaicite:5]{index=5}
- Configure clip player settings :contentReference[oaicite:6]{index=6}
- Fuzzy search by clip name/terms :contentReference[oaicite:7]{index=7}

### 1.2 Command requirements (current state)
- `!watch <clip-link>` plays a clip and auto-creates OBS scene/source if missing :contentReference[oaicite:8]{index=8}
- `!watch @broadcasterName <search terms>` searches clips and plays best match (mod approval gate exists today) :contentReference[oaicite:9]{index=9}
- `!so <username>` plays a random clip from that channel using featured-first then ranges; may auto-fire on raid :contentReference[oaicite:10]{index=10}
- `!replay` replays most recent clip :contentReference[oaicite:11]{index=11}
- `!stop` stops playback; note existing “delay bug” called out in README :contentReference[oaicite:12]{index=12}

### 1.3 Settings requirements (current state)
- Scene/player dimensions (default 1920×1080) with auto-fit to 16:9 and letterboxing/pillarboxing :contentReference[oaicite:13]{index=13}
- Debug logging :contentReference[oaicite:14]{index=14}
- Shoutout message configurable; disable by setting to empty string :contentReference[oaicite:15]{index=15}
- Shoutout “use featured clips” toggle :contentReference[oaicite:16]{index=16}
- Shoutout clip max length (seconds) and max age (days) filters :contentReference[oaicite:17]{index=17}

---

## 2) New Product Requirements (Your Preferences)

### 2.1 Reliability (“Just Works”)
- Detect failures automatically (Twitch, OBS, player page, local server).
- Self-repair automatically (reconnect, recreate missing OBS objects, refresh sources, retry with backoff).
- Degrade gracefully (skip a bad clip, fall back to alternate Twitch transport) without requiring stream-time user action.

### 2.2 Modern-first, stability-aware
- Prefer Twitch EventSub WebSocket for event intake where appropriate (modern surface). :contentReference[oaicite:18]{index=18}
- Use Helix for clips operations. :contentReference[oaicite:19]{index=19}
- Use official chat send API where feasible. :contentReference[oaicite:20]{index=20}
- Use OBS built-in WebSocket (OBS 28+ includes it) for automation/control. :contentReference[oaicite:21]{index=21}
- Keep a tested fallback path (ex: IRC intake) if modern path degrades. :contentReference[oaicite:22]{index=22}

### 2.3 Distribution / UX
- Windows tray app (interactive, streamer-friendly).
- “Minimal setup”: connect Twitch, connect OBS, choose sizing/theme/messages, done.

---

## 3) Proposed Architecture (Agent-Readable)

### 3.1 Processes / components (single local app)
1. **Tray Host**
    - App lifecycle, tray UI, status, “Export diagnostics”.
2. **Local Player Server**
    - Serves `http://127.0.0.1:<port>/` for OBS Browser Source.
3. **Clip Engine**
    - Parses commands → resolves clip(s) → queue + playback state machine.
4. **Twitch Integration**
    - OAuth + token lifecycle; Helix client; event intake (EventSub WS primary, IRC fallback).
5. **OBS Integration**
    - Connects to obs-websocket; enforces “desired state” (scene/source exists & correct).
6. **Health + Self-Repair Supervisor**
    - Periodic health checks, reconnection loops, drift correction, backoff policies.

### 3.2 Key internal contracts (interfaces)
- `ITwitchAuthStore` (securely store refresh tokens; rotate; invalidate)
- `ITwitchHelixClient` (clips lookup/search; chat send)
- `ITwitchEventSource` (EventSubWS | IRC; normalized events)
- `IObsController` (connect; ensure sources; refresh browser source)
- `IClipQueue` (enqueue; dequeue; replay; cancel; quarantine bad clips)
- `IHealthReporter` (component status, last error, repair actions taken)

### 3.3 State model (simplified)
- `Idle → Loading → Playing → Cooldown → Idle`
- Interrupts:
    - `StopRequested` (stops current, advances queue)
    - `Error` (quarantine clip; optional chat notice; advance queue)
    - `ObsDisconnected` (attempt repair while preserving queue)

---

## 4) Technology Choices (Modern, Stable)

### 4.1 Runtime + language
- Target: .NET (LTS preferred), modern C# (post Streamer.bot constraints).
- Async-first design (Channels for event bus; background services for supervisors).

### 4.2 Twitch transport
- Primary intake: EventSub WebSocket (modern). :contentReference[oaicite:23]{index=23}
- Fallback intake: IRC receive path for commands (lifeboat). :contentReference[oaicite:24]{index=24}
- Clips: Helix “Get Clips” as canonical. :contentReference[oaicite:25]{index=25}
- Chat send: “Send Chat Message” API. :contentReference[oaicite:26]{index=26}

### 4.3 OBS control
- obs-websocket protocol via built-in endpoint (OBS 28+). :contentReference[oaicite:27]{index=27}
- “Desired state” enforcement:
    - Ensure scene exists
    - Ensure browser source exists with correct URL and dimensions
    - Refresh/recreate if drift detected

---

## 5) Work Plan (Milestones + Acceptance Criteria)

> Milestones are ordered to keep the product usable as early as possible while building the reliability backbone first.

### Milestone 0 — Repo alignment & baseline inventory (1–2 sessions)
**Deliverables**
- Map existing code modules (`src`, managers) to the new components list.
- Document “parity targets” as a checklist (see §1).
- Decide what remains: local player HTML/CSS vs rewrite.

**Acceptance**
- Single checklist that traces every README feature/setting/command to an implementation target.

### Milestone 1 — “Player + Queue” MVP (no Twitch/OBS automation yet)
**Deliverables**
- Local HTTP server hosting a player page for an OBS browser source.
- Clip queue engine with `!watch`-equivalent internal API.
- Playback state machine; “replay”; “stop”.

**Acceptance**
- Given a clip URL/ID, the app can play it reliably in the browser page, enqueue multiple, and replay last.

Traceability: play specific clips, enqueue, stop, replay. :contentReference[oaicite:28]{index=28}

### Milestone 2 — Twitch OAuth + Helix clips (data plane)
**Deliverables**
- OAuth login flow from tray UI.
- Token refresh + storage + retry policy.
- Helix Get Clips integration; clip metadata fetch.

**Acceptance**
- App can resolve and validate clip IDs/URLs; gracefully skip non-existent clips.
- No user re-login during normal token refresh.

Traceability: play clip by link; shoutout clip selection inputs require clips queries. :contentReference[oaicite:29]{index=29}

### Milestone 3 — Twitch events + commands (control plane)
**Deliverables**
- Event source abstraction:
    - EventSub WS primary (where appropriate). :contentReference[oaicite:30]{index=30}
    - IRC fallback for command intake. :contentReference[oaicite:31]{index=31}
- Command router implementing:
    - `!watch`, `!stop`, `!replay`, `!so`

**Acceptance**
- Commands work end-to-end from chat → playback.
- If EventSub intake is degraded, IRC fallback keeps commands working.

Traceability: play clips posted in chat; command set. :contentReference[oaicite:32]{index=32}

### Milestone 4 — OBS automation & drift repair
**Deliverables**
- OBS websocket connection manager.
- “Ensure clip scene & browser source exists” behavior (auto-create if missing).
- Periodic drift check (URL/dimensions mismatch; missing objects; OBS restart).
- “Refresh browser source” repair action.

**Acceptance**
- Fresh OBS profile: app can create required scene/source automatically.
- After OBS restart: app reconnects and self-heals without user action.

Traceability: README states missing scene/source auto-creation today. :contentReference[oaicite:33]{index=33}

### Milestone 5 — Shoutouts parity (clip selection policy + messaging)
**Deliverables**
- Shoutout queue separate from normal queue.
- Selection policy:
    - Featured-first toggle
    - Max clip length + max age filters :contentReference[oaicite:34]{index=34}
- Chat output:
    - Configurable shoutout message (disable with empty string). :contentReference[oaicite:35]{index=35}
    - Optional `/shoutout` behavior (subject to auth/permissions).

**Acceptance**
- `!so` reliably plays appropriate clips and sends the message.
- Filters are enforced; long/old clips are excluded.

Traceability: shoutouts + settings. :contentReference[oaicite:36]{index=36}

### Milestone 6 — Fuzzy search + mod-approval gate parity
**Deliverables**
- Clip search by terms for `!watch @channel terms`.
- Approval workflow (agent note: this may require a UX decision—e.g., a short-lived approval prompt in chat, or tray toast actions).

**Acceptance**
- Search returns stable results; approval gate prevents abuse by default (matching current behavior). :contentReference[oaicite:37]{index=37}

### Milestone 7 — “Just Works” polish (diagnostics, updates, guardrails)
**Deliverables**
- Structured logs + rotating files.
- Diagnostics exporter (bundle config + logs + recent errors, with token redaction).
- Backoff/jitter policies for all reconnect loops.
- Optional auto-update channel (or at least “check for updates” UX).

**Acceptance**
- Common failures self-heal:
    - Twitch disconnect
    - OBS disconnect
    - Bad clip
    - Player hang (source refresh)
- Users can submit a diagnostics bundle without handholding.

---

## 6) Reliability Design Rules (Non-negotiable)

1. **Never block the streamer mid-stream**: no modal dialogs during runtime.
2. **Backoff everything**: reconnect loops must use exponential backoff + jitter.
3. **Quarantine bad clips**: repeated failures on the same clip should not stall queues.
4. **Desired-state OBS management**: treat OBS config as drift-prone and enforce it continuously.
5. **Transport redundancy**: one Twitch intake method failing must not equal “tool dead”.

---

## 7) Traceability Matrix (README → New System)

> Keep this list updated as implementation proceeds.

- Play specific clips → Milestones 1–3 :contentReference[oaicite:38]{index=38}
- Enqueue clips → Milestone 1 :contentReference[oaicite:39]{index=39}
- Play clips posted in c~~~~hat → Milestone 3 :contentReference[oaicite:40]{index=40}
- Stop playback → Milestone 1/3 :contentReference[oaicite:41]{index=41}
- Replay → Milestone 1/3 :contentReference[oaicite:42]{index=42}
- Shoutouts + `/shoutout` → Milestone 5 :contentReference[oaicite:43]{index=43}
- Settings (dimensions, logging, filters, shoutout message) → Milestones 4–5 :contentReference[oaicite:44]{index=44}
- Fuzzy search / approval gate → Milestone 6 :contentReference[oaicite:45]{index=45}

---

## 8) Open Questions (Keep to Minimum)

1. Is “play clips other users post in chat” limited to mods/VIPs by default, or open? (Current README implies general behavior; implement guardrails.)
2. Should the approval gate be chat-based, tray-based, or both?
3. Should the tray app offer “OBS source placement helpers” (auto center, fit, etc.) or keep it strictly minimal?

(Answering these can be deferred until Milestone 6 without blocking earlier milestones.)

---
