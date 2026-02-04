# Milestone 0: Repo Alignment & Baseline Inventory

## 1. Existing Code → New Component Mapping

### Current Architecture (Streamer.bot Plugin)
**Entry Point**: [`CPHInline.cs`](./Cliparino/src/CPHInline.cs)
- Main command handler (Execute method)
- Manages component lifecycle
- Processes commands: `!watch`, `!                                                                                                                                                                                                                                         so`, `!replay`, `!stop`
- Handles mod approval workflow

**Managers**:
- [`ClipManager.cs`](./Cliparino/src/Managers/ClipManager.cs) - Clip data fetching, caching, random selection
- [`TwitchApiManager.cs`](./Cliparino/src/Managers/TwitchApiManager.cs) - Twitch Helix API, OAuth, shoutouts
- [`ObsSceneManager.cs`](./Cliparino/src/Managers/ObsSceneManager.cs) - OBS scene/source management via Streamer.bot
- [`HttpManager.cs`](./Cliparino/src/Managers/HttpManager.cs) - HTTP server, player HTML/CSS, content warning handling
- [`CPHLogger.cs`](./Cliparino/src/Managers/CPHLogger.cs) - Logging to Streamer.bot

**Utilities**:
- [`ConfigurationManager.cs`](./Cliparino/src/Utilities/ConfigurationManager.cs) - Settings management
- [`ErrorHandler.cs`](./Cliparino/src/Utilities/ErrorHandler.cs) - Error handling utilities
- [`HttpResponseBuilder.cs`](./Cliparino/src/Utilities/HttpResponseBuilder.cs) - HTTP response formatting
- [`InputProcessor.cs`](./Cliparino/src/Utilities/InputProcessor.cs) - Command/input parsing
- [`RetryHelper.cs`](./Cliparino/src/Utilities/RetryHelper.cs) - Retry logic with backoff
- [`ValidationHelper.cs`](./Cliparino/src/Utilities/ValidationHelper.cs) - Input validation

**Constants**:
- [`CliparinoConstants.cs`](./Cliparino/src/Constants/CliparinoConstants.cs) - Centralized constants

---

### New Architecture (Tray App) Mapping

| New Component | Maps From Existing | Notes |
|---------------|-------------------|-------|
| **Tray Host** | _NEW_ | Windows tray app lifecycle, settings UI |
| **Local Player Server** | `HttpManager.cs` (HTTP server portion) | Extract HTTP server, keep player HTML/CSS with modernization |
| **Clip Engine** | `ClipManager.cs` + `CPHInline.cs` (command logic) | Merge clip operations + queue state machine |
| **Twitch Integration** | `TwitchApiManager.cs` + _NEW EventSub/IRC_ | Keep Helix client, add EventSub WS + IRC fallback |
| **OBS Integration** | `ObsSceneManager.cs` | Replace Streamer.bot OBS calls with obs-websocket client |
| **Health + Self-Repair Supervisor** | _NEW_ + `RetryHelper.cs` | New supervisor loop, reuse retry patterns |

---

## 2. Player HTML/CSS Decision

### Current Implementation
**Location**: Embedded in [`HttpManager.cs:84-138`](./Cliparino/src/Managers/HttpManager.cs:84) (CSS) and [`HttpManager.cs:140-350+`](./Cliparino/src/Managers/HttpManager.cs:140) (HTML + JavaScript)

**Features**:
- ✅ Twitch embed iframe with autoplay
- ✅ Overlay text (streamer name, game, clip title, curator)
- ✅ Content warning detection + OBS automation workaround
- ✅ Responsive 16:9 layout with letterboxing/pillarboxing
- ✅ Custom fonts (OpenDyslexic, Open Sans)

**Quality Assessment**:
- Clean, functional HTML structure
- CSS is well-organized with good color scheme
- JavaScript has sophisticated content warning detection
- Already handles parent domain for Twitch embed

### Decision: **REUSE with Minor Modernization**

**Rationale**:
1. Current implementation already handles all requirements (16:9, overlay, content warning)
2. No UX issues reported - "just works" design
3. Modernization effort should focus on backend reliability, not frontend rewrite
4. Time saved can go toward health monitoring and self-repair

**Planned Updates**:
- Extract HTML/CSS/JS to separate files (better maintainability)
- Update template placeholders to use a proper templating approach
- Keep content warning detection logic (proven in production)
- Add connection health indicators if needed
- Modernize JavaScript (optional: use ES6+ features, but maintain compatibility)

---

## 3. Feature Parity Checklist

### Core Features (README §5-19)

| Feature | Current Implementation | New Target Component | Status |
|---------|----------------------|---------------------|--------|
| Play specific clips | `CPHInline.Execute()` → `ClipManager` → `ObsSceneManager.PlayClipAsync()` | Clip Engine + OBS Integration | 📋 Planned (M1-3) |
| Enqueue clips for playback | `ClipManager` (implicit queue via global vars) | Clip Engine (IClipQueue) | 📋 Planned (M1) |
| Play clips posted in chat | `CPHInline.Execute()` (watches for chat commands via Streamer.bot) | Twitch Integration (EventSub/IRC) → Clip Engine | 📋 Planned (M3) |
| Shoutouts (random clip) | `CPHInline.Shoutout()` → `ClipManager.GetRandomClipAsync()` | Clip Engine (separate shoutout queue) | 📋 Planned (M5) |
| Stop clip playback | `CPHInline.StopClip()` → `ObsSceneManager.StopClipAsync()` | Clip Engine (state machine) | 📋 Planned (M1/M3) |
| Configure settings | Streamer.bot Action sub-actions (manual) | Tray UI (settings dialog) | 📋 Planned (M1+) |
| Fuzzy search by clip name | `ClipManager.FindClipBySearchTermsAsync()` | Clip Engine + Twitch Integration | 📋 Planned (M6) |

### Commands (README §34-68)

| Command | Current Logic | New Implementation | Status |
|---------|--------------|-------------------|--------|
| `!watch <clip-link>` | `CPHInline.WatchClip()` → parse URL → play | EventSub/IRC → Command Router → Clip Engine | 📋 M3 |
| `!watch @broadcaster terms` | `CPHInline.WatchClip()` → fuzzy search → mod approval | EventSub/IRC → Command Router → Approval Gate → Clip Engine | 📋 M6 |
| `!so <username>` | `CPHInline.Shoutout()` → fetch random → play + chat message | EventSub/IRC → Command Router → Clip Engine (shoutout queue) | 📋 M5 |
| `!replay` | `CPHInline.Replay()` → fetch last URL → play | EventSub/IRC → Command Router → Clip Engine | 📋 M1/M3 |
| `!stop` | `CPHInline.StopClip()` → hide OBS source | EventSub/IRC → Command Router → Clip Engine → OBS Integration | 📋 M1/M3 |

### Settings (README §71-83)

| Setting | Current Storage | New Implementation | Status |
|---------|----------------|-------------------|--------|
| Scene dimensions (default 1920×1080, 16:9 auto-fit) | Streamer.bot Action variables | Tray app config file + UI | 📋 M1/M4 |
| Debug logging toggle | Streamer.bot Action variable | Tray app config file + UI | 📋 M1 |
| Shoutout message (configurable, "" to disable) | Streamer.bot Action variable | Tray app config file + UI | 📋 M5 |
| Shoutout: Use featured clips toggle | Streamer.bot Action variable | Tray app config file + UI | 📋 M5 |
| Shoutout: Max clip length (seconds) | Streamer.bot Action variable (default 30) | Tray app config file + UI | 📋 M5 |
| Shoutout: Max clip age (days) | Streamer.bot Action variable (default 30) | Tray app config file + UI | 📋 M5 |

### OBS Automation (README §36-38, §89-95)

| Behavior | Current Implementation | New Implementation | Status |
|----------|----------------------|-------------------|--------|
| Auto-create missing scene | `ObsSceneManager.EnsureCliparino()` | OBS Integration (desired-state enforcement) | 📋 M4 |
| Auto-create missing browser source | `ObsSceneManager.EnsureSource()` | OBS Integration (desired-state enforcement) | 📋 M4 |
| Refresh browser source (drift repair) | `ObsSceneManager.RefreshSource()` | OBS Integration (periodic drift check) | 📋 M4 |
| Content warning handling | `HttpManager.cs` (JS detection + OBS automation) | Keep player JS detection + OBS websocket interaction | 📋 M1/M4 |

---

## 4. Reusable Code Inventory

### High-Value Reuse Candidates

| File | Reusable Logic | Adaptation Needed |
|------|---------------|-------------------|
| `ClipManager.cs` | Clip caching, random selection policy (featured-first, filters), clip metadata handling | Remove Streamer.bot dependencies; adapt to new ITwitchHelixClient interface |
| `TwitchApiManager.cs` | Helix API client patterns, OAuth flow, shoutout message formatting | Extract Streamer.bot `IInlineInvokeProxy` dependencies; add EventSub WS + IRC |
| `HttpManager.cs` | HTTP server (port retry logic), player HTML/CSS/JS, content warning detection | Extract to separate server component; separate HTML/CSS/JS files |
| `RetryHelper.cs` | Exponential backoff, retry with cancellation | Direct reuse (add jitter if missing) |
| `ValidationHelper.cs` | Input sanitization (usernames, clip IDs) | Direct reuse |
| `InputProcessor.cs` | Command parsing logic | Adapt to new event source abstraction |
| `ErrorHandler.cs` | Error categorization patterns | Adapt to new health monitoring system |

### Low-Value / Don't Reuse

| File | Reason |
|------|--------|
| `CPHInline.cs` | Tightly coupled to Streamer.bot API; command routing logic will be reimplemented |
| `ObsSceneManager.cs` | Uses Streamer.bot's OBS wrapper; must be rewritten for obs-websocket protocol |
| `CPHLogger.cs` | Streamer.bot-specific logging; replace with standard .NET logging (ILogger) |
| `ManagerFactory.cs` | Dependency injection for Streamer.bot context; replace with modern DI container |

---

## 5. Technology Decisions

### Runtime
- **Target Framework**: .NET 8 (LTS) or .NET 9 (latest stable)
- **Language**: C# 12+ (modern features: primary constructors, collection expressions, etc.)
- **UI**: Windows Forms or WPF for tray app (lightweight, native feel)

### Libraries
- **HTTP Server**: ASP.NET Core Kestrel (embedded, self-hosted)
- **OBS Control**: `obs-websocket-dotnet` or `OBSWebsocketDotNet` NuGet package
- **Twitch EventSub**: Custom implementation or `TwitchLib.EventSub.Websockets`
- **IRC Fallback**: `TwitchLib.Client` (proven, stable)
- **Logging**: `Microsoft.Extensions.Logging` with file provider
- **Configuration**: `Microsoft.Extensions.Configuration` with JSON file
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`

---

## 6. Open Questions for Milestone 0

### Question 1: Player HTML Hosting
**Current**: Embedded strings in `HttpManager.cs`  
**Proposed**: Extract to separate files (`wwwroot/index.html`, `wwwroot/index.css`, `wwwroot/player.js`)

**Decision**: Extract to separate files for maintainability. Use embedded resources for distribution simplicity.

### Question 2: Settings Storage
**Current**: Streamer.bot Action variables (ephemeral, UI-based)  
**Proposed**: JSON config file in `%AppData%\Cliparino\settings.json`

**Decision**: Use JSON config file with `IConfiguration`. Tray UI provides CRUD.

### Question 3: OBS Websocket Version
**Current**: N/A (uses Streamer.bot's OBS wrapper)  
**Proposed**: Target obs-websocket 5.x (OBS 28+, built-in)

**Decision**: Target obs-websocket 5.x. Document minimum OBS version (28+) in requirements.

---

## Acceptance Criteria for Milestone 0

- ✅ **Single checklist**: Traces every README feature/setting/command to implementation target (see §3)
- ✅ **Component mapping**: Maps existing modules to new architecture (see §1)
- ✅ **Player decision**: Decision made on HTML/CSS reuse vs. rewrite (see §2)
- ✅ **Reusable code identified**: High-value reuse candidates documented (see §4)
- ✅ **Technology stack chosen**: Runtime, libraries, frameworks decided (see §5)

**Status**: ✅ **Milestone 0 Complete** - Ready to proceed to Milestone 1
