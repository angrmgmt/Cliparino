# Cliparino

**A standalone Twitch clip player with intelligent search, queue management, and OBS integration - built for reliability.**

![License](https://img.shields.io/badge/license-LGPL%202.1-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![C#](https://img.shields.io/badge/C%23-Latest-239120)

## Overview

Cliparino is a standalone Windows tray application for playing Twitch clips during streams. Built with a "just works" philosophy, it provides intelligent clip search, automatic OBS integration, self-healing reliability, and comprehensive queue management.

> **Note**: This describes the **modern rewrite** (.NET 8). The legacy Streamer.bot version is archived in [`/legacy/`](./legacy/). See [Development Status](#development-status) for progress.

## Key Features

### Core Functionality
- **Direct Clip Playback**: Play specific clips from Twitch.tv using URLs
- **Smart Search**: Find clips by name using advanced fuzzy search with word-based similarity matching
- **Chat Integration**: Automatically detect and play clips posted in chat
- **Queue Management**: Sequential playback of multiple enqueued clips
- **Replay Command**: Instantly replay the most recently played clip
- **Moderator Approval**: Configurable approval system for searched clips

### Shoutout System
- **Automatic Shoutouts**: Trigger on raids or manual `!so` command
- **Smart Clip Selection**: Prioritize featured clips with fallback to recent/short clips
- **Customizable Messages**: Template-based chat messages with placeholders
- **Native Integration**: Uses Twitch `/shoutout` command
- **Separate Queue**: Independent from regular clip playback

### OBS Integration
- **Automatic Scene Management**: Creates/manages scenes and sources automatically
- **Flexible Display**: Configurable dimensions (auto 16:9 aspect ratio)
- **Browser Source**: Clips served via local HTTP server
- **Drift Repair**: Auto-corrects configuration mismatches

### Technical Features
- **Intelligent Caching**: Reduces API calls, improves search performance
- **Retry Logic**: Exponential backoff for failed operations
- **Comprehensive Logging**: Debug logging for troubleshooting
- **Self-Healing**: Automatic reconnection, drift repair, bad clip quarantine
- **Modular Architecture**: Clean separation of concerns with dependency injection

## Requirements

- **Windows**: Windows 10 or later
- **OBS Studio**: Version 28+ (includes built-in WebSocket server)
- **.NET Runtime**: 8.0 LTS (installer will prompt if needed)
- **Twitch Account**: For OAuth authentication

## Installation

> **⚠️ Development Status**: The modern rewrite is currently under active development. End-user releases are not yet available. See [Development Status](#development-status) below for progress.

### For End Users (Coming Soon)

Installation will be:
1. Download the installer from Releases
2. Run the installer (includes .NET 8 if needed)
3. Launch Cliparino from system tray
4. Connect Twitch account via OAuth
5. Connect to OBS (automatically detects local OBS instance)
6. Configure settings and start using commands

### For Developers

See the [Development](#development) section below for setup instructions.

## Usage

### Commands

#### `!watch <clip-link>`
Play a specific Twitch clip by URL.

**Examples:**
```
!watch https://clips.twitch.tv/BetterTwitchClips-AbC123XyZ
!watch https://www.twitch.tv/broadcaster/clip/ClipSlugHere
```

**Behavior:**
- Creates OBS scene and source automatically if they don't exist
- Adds clip to playback queue
- Source can be copied to other scenes
- Falls back to last posted clip URL if no URL provided

#### `!watch @username search terms`
Search for clips by name using fuzzy search.

**Examples:**
```
!watch @shroud ace
!watch headshot
```

**Behavior:**
- With `@username`: Searches that broadcaster's clips
- Without username: Searches your own channel's clips
- Uses advanced word-based similarity matching
- **Requires moderator approval** by default
- Moderators can approve with: `yes`, `yep`, `yeah`, `sure`, `okay`, `go ahead`, etc.
- Moderators can deny with: `no`, `nope`, `nah`, `not okay`, etc.
- Cache stores search results for faster repeated searches

**Important:** You **must** use the `@` symbol before the username.

#### `!so <username>`
Perform a shoutout with a random clip from the user's channel.

**Examples:**
```
!so shroud
!so pokimane
```

**Behavior:**
- Plays a random clip from the user's channel
- Prioritizes **Featured** clips if enabled in settings
- Filters by max duration and clip age (configurable)
- Sends customizable chat message with channel link
- Executes Twitch's native `/shoutout` command
- Separate queue from `!watch` clips
- Can trigger automatically on raids (configurable)

#### `!replay`
Replay the most recently played clip.

**Example:**
```
!replay
```

**Behavior:**
- Fetches last played clip from cache
- Re-enqueues for playback
- Works with both `!watch` and `!so` clips

#### `!stop`
Stop the currently playing clip.

**Example:**
```
!stop
```

**Behavior:**
- Immediately stops clip playback
- Clears OBS browser source
- Stops HTTP server hosting
- Next queued clip will play automatically

### Configuration

Cliparino is configured via `appsettings.json` (located next to the executable). Access settings through the tray menu. **Restart required** after changing settings.

### Common Settings

| Key | Default | Description |
|-----|---------|-------------|
| `Obs:Host` | `localhost` | OBS WebSocket host |
| `Obs:Port` | `4455` | OBS WebSocket port |
| `Obs:Password` | *(empty)* | OBS WebSocket password |
| `Player:SceneName` | `Cliparino` | OBS scene name used/managed by Cliparino |
| `Player:SourceName` | `Cliparino Player` | OBS browser source name used/managed by Cliparino |
| `Player:Width` | `1920` | Player viewport width (pixels) |
| `Player:Height` | `1080` | Player viewport height (pixels) |
| `Shoutout:EnableMessage` | `true` | Whether Cliparino sends a shoutout chat message |
| `Shoutout:MessageTemplate` | `Check out {broadcaster}! ...` | Shoutout message template (supports placeholders) |
| `Shoutout:UseFeaturedClips` | `true` | Prefer featured clips for shoutouts when available |
| `Shoutout:MaxClipLength` | `60` | Max clip duration used for shoutout selection (seconds) |
| `Shoutout:MaxClipAge` | `30` | Max clip age used for shoutout selection (days) |
| `Logging:LogLevel:Default` | `Information` | Application log level |

### Example `appsettings.json`

```json
{
  "Obs": {
    "Host": "localhost",
    "Port": "4455",
    "Password": ""
  },
  "Player": {
    "SceneName": "Cliparino",
    "SourceName": "Cliparino Player",
    "Width": "1920",
    "Height": "1080"
  },
  "Shoutout": {
    "EnableMessage": "true",
    "MessageTemplate": "Check out {broadcaster}! They were last playing {game}! twitch.tv/{broadcaster}",
    "UseFeaturedClips": "true",
    "MaxClipLength": "60",
    "MaxClipAge": "30"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

## HTTP API

The local web host listens on `http://localhost:5290`.

### Player endpoints (`/api`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/status` | Current player state, current clip (if any), and queue size |
| POST | `/api/play` | Enqueue a clip for playback (body: `PlayClipRequest`) |
| POST | `/api/replay` | Replay the most recently played clip |
| POST | `/api/stop` | Stop playback |
| POST | `/api/content-warning` | Record a content-warning signal (currently informational) |

**POST `/api/play` body**

```json
{
  "clipId": "https://clips.twitch.tv/...",
  "title": "Optional",
  "creatorName": "Optional",
  "broadcasterName": "Optional",
  "gameName": "Optional",
  "durationSeconds": 30
}
```

### Health endpoint

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/health` | Aggregated health for core components and integrations |

### Diagnostics endpoints (`/api/diagnostics/export`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/diagnostics/export` | Export a plain-text diagnostics report |
| GET | `/api/diagnostics/export/zip` | Export diagnostics as a ZIP archive |

### Update endpoints (`/api/update`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/update/check` | Check for updates and return the latest release metadata |
| GET | `/api/update/current` | Return the current running version |

### Auth endpoints (`/auth`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/auth/login` | Return the OAuth authorization URL |
| GET | `/auth/callback` | OAuth callback used by Twitch redirect (returns HTML) |
| GET | `/auth/status` | Whether Cliparino is authenticated |
| POST | `/auth/logout` | Clear stored authentication state |


## Architecture

### Component Interaction Flow

#### Clip playback flow
```
Chat / API
   → TwitchEventCoordinator
       → CommandRouter
           → PlaybackEngine
               → ObsController (scene/source management)
                   → Player page (browser source)
```

#### Self-healing flow
```
ObsHealthSupervisor detects issue
   → reconnect / backoff
   → drift detection
   → desired-state repair (scene/source re-creation / configuration)
```

#### Failover flow
```
EventSub WebSocket degraded
   → TwitchEventCoordinator switches to IRC
   → continue emitting TwitchEvent stream to CommandRouter
```


### Repository Structure

```
Cliparino/
├── src/                           # Modern rewrite (canonical)
│   ├── Cliparino.Core/           # Main application (.NET 8)
│   │   ├── Controllers/          # HTTP API controllers
│   │   ├── Models/               # Data models
│   │   ├── Services/             # Core services & background workers
│   │   ├── UI/                   # System tray application UI
│   │   ├── wwwroot/              # Static web files (player page)
│   │   ├── Program.cs            # Application entry point
│   │   └── appsettings.json      # Configuration template
│   └── tests/
│       └── Cliparino.Core.Tests/ # Unit tests
├── docs/                          # Development documentation
│   ├── PLAN.MD                   # Development roadmap
│   ├── PARITY_CHECKLIST.md       # Feature parity tracking
│   └── MILESTONE_*.md            # Milestone completion reports
├── legacy/                        # Legacy Streamer.bot code (archived)
│   ├── Cliparino/                # Original inline script project (.NET Framework 4.7.2)
│   └── FileProcessor/            # Build utilities for legacy version
├── Cliparino.sln                 # Solution file
└── README.md                     # This file
```

### Core Components

#### **Tray Host** ([`TrayApplicationContext.cs`](./src/Cliparino.Core/UI/TrayApplicationContext.cs))
Windows system tray application providing lifecycle management, status display, and diagnostics export.

#### **Local Player Server** ([`Program.cs`](./src/Cliparino.Core/Program.cs))
ASP.NET Core web host serving the clip player page at `http://localhost:5290/` for OBS Browser Source, plus HTTP APIs for control and monitoring.

#### **Clip Engine** ([`PlaybackEngine.cs`](./src/Cliparino.Core/Services/PlaybackEngine.cs), [`ClipQueue.cs`](./src/Cliparino.Core/Services/ClipQueue.cs))
Command routing, clip resolution, queue management (FIFO playback), and playback state machine.

#### **Twitch Integration** ([`TwitchHelixClient.cs`](./src/Cliparino.Core/Services/TwitchHelixClient.cs), [`TwitchEventCoordinator.cs`](./src/Cliparino.Core/Services/TwitchEventCoordinator.cs))
OAuth 2.0 authentication with token refresh, Helix API client, EventSub WebSocket (primary), IRC fallback, and chat message sending.

#### **OBS Integration** ([`ObsController.cs`](./src/Cliparino.Core/Services/ObsController.cs), [`ObsHealthSupervisor.cs`](./src/Cliparino.Core/Services/ObsHealthSupervisor.cs))
obs-websocket client, desired-state enforcement (auto-create/repair scenes & sources), browser source management, and drift detection/correction.

#### **Health & Self-Repair** ([`HealthReporter.cs`](./src/Cliparino.Core/Services/HealthReporter.cs), [`ObsHealthSupervisor.cs`](./src/Cliparino.Core/Services/ObsHealthSupervisor.cs))
Periodic health checks, automatic reconnection with exponential backoff, drift correction, and bad clip quarantine.

### Design Principles

- **"Just Works" Philosophy**: Automatic detection, creation, and repair of resources
- **Self-Healing**: Reconnect on failures, repair drift, quarantine bad clips
- **Modern APIs**: EventSub WebSocket, Helix, obs-websocket (OBS 28+)
- **Graceful Degradation**: Fallback paths (IRC) when modern transports fail
- **Separation of Concerns**: Clean interfaces between components
- **Testability**: Dependency injection and comprehensive unit tests

## Troubleshooting

- **Search Issues**: Check [existing issues](https://github.com/angrmgmt/Cliparino/issues)
- **Debug Logging**: Set `Logging:LogLevel:Default` to `Debug` in `appsettings.json`
- **Report Bug**: [Create an issue](https://github.com/angrmgmt/Cliparino/issues/new) with .NET version, OBS version, steps to reproduce, and log excerpts (redact tokens)

## Development Status

The modern rewrite is progressing through phased milestones. See [`/docs/PLAN.MD`](./docs/PLAN.MD) for the complete roadmap and [`/docs/PARITY_CHECKLIST.md`](./docs/PARITY_CHECKLIST.md) for feature parity tracking.

### Milestone Progress

| Milestone | Status | Description |
|-----------|--------|-------------|
| M0 | ✅ Complete | Repo alignment & baseline inventory |
| M1 | ✅ Complete | Player + Queue MVP |
| M2 | ✅ Complete | Twitch OAuth + Helix clips |
| M3 | ✅ Complete | Twitch events + commands |
| M4 | ✅ Complete | OBS automation & drift repair |
| M5 | ✅ Complete | Shoutouts parity |
| M6 | ✅ Complete | Fuzzy search + mod-approval gate |
| M7 | ✅ Complete | "Just Works" polish |

> All core milestones achieved! Now ready for packaging, deployment, and end-user testing.

## Development

### Quick Start

```bash
# Clone repository
git clone https://github.com/angrmgmt/Cliparino.git
cd Cliparino

# Build project
dotnet build Cliparino.sln

# Run tests
dotnet test Cliparino.sln

# Run application
dotnet run --project src/Cliparino.Core
```

**Prerequisites**: .NET 8.0 SDK, Windows 10+, OBS Studio 28+

**IDE Options**: Visual Studio 2022+, JetBrains Rider, or VS Code with C# Dev Kit

### Code Conventions

- **Language**: C# (latest features)
- **Framework**: .NET 8.0 LTS (`net8.0-windows`)
- **Style**: One True Brace Style (OTB), PascalCase for public APIs, `_camelCase` for private fields
- **Async**: Use async/await for all I/O operations
- **Docs**: XML comments for public APIs

See [CONTRIBUTING.md](./CONTRIBUTING.md) for detailed guidelines on code style, testing, and pull requests.

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](./CONTRIBUTING.md) for guidelines on code style, testing, and pull requests. For major changes, please open an issue first to discuss your proposal.

## License

Cliparino is licensed under the [LGPL 2.1](./LICENSE).

Copyright (C) 2024 Scott Mongrain - angrmgmt@gmail.com

## Acknowledgments

- [**Streamer.bot**](https://streamer.bot/) - The original Cliparino was built on Streamer.bot. The modern rewrite is standalone, but this platform inspired the project.
- [**OBS Studio**](https://obsproject.com/) - For the streaming infrastructure
- [**Twitch**](https://www.twitch.tv/) - For the API and platform

## Links

- **Repository**: [github.com/angrmgmt/Cliparino](https://github.com/angrmgmt/Cliparino)
- **Releases**: [Latest Release](https://github.com/angrmgmt/Cliparino/releases/latest)
- **Issues**: [Report a Bug](https://github.com/angrmgmt/Cliparino/issues/new)
- **Discussions**: [GitHub Discussions](https://github.com/angrmgmt/Cliparino/discussions)
- **Contact**: angrmgmt@gmail.com
