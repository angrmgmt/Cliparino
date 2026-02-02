# Cliparino

**A standalone Twitch clip player with intelligent search, queue management, and OBS integration - built for reliability.**

![License](https://img.shields.io/badge/license-LGPL%202.1-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![C#](https://img.shields.io/badge/C%23-Latest-239120)

## Overview

Cliparino is a modern, standalone Windows tray application for playing Twitch clips during streams. Built with a "just works" philosophy, it provides intelligent clip search, automatic OBS integration, self-healing reliability, and comprehensive queue management.

> **Note**: This README describes the **modern rewrite** (currently in active development). The legacy Streamer.bot-based version is archived in [`/legacy/`](./legacy/) for reference. See [Development Status](#development-status) below for current progress.

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
- **Smart Clip Selection**: Prioritize featured clips with configurable fallback ranges
- **Customizable Messages**: Configurable chat messages with channel links
- **Native Integration**: Uses Twitch's built-in `/shoutout` command
- **Separate Queue**: Shoutouts don't interfere with regular clip queue

### OBS Integration
- **Automatic Scene Management**: Creates and manages scenes and sources automatically
- **Flexible Display**: Configurable dimensions with automatic 16:9 aspect ratio handling
- **Browser Source**: Clips served via embedded HTTP server for reliable playback
- **Multi-Scene Support**: Copy sources to any scene as needed

### Technical Features
- **Intelligent Caching**: Reduces API calls and improves search performance
- **Retry Logic**: Automatic retry with exponential backoff for failed operations
- **Comprehensive Logging**: Detailed debug logging for troubleshooting
- **Error Recovery**: Graceful error handling with user-friendly messages
- **Modular Architecture**: Clean separation of concerns with dedicated managers

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

All settings are accessible in Streamer.bot under the **Cliparino** action → **Sub-Actions**:

| Setting | Default | Description |
|---------|---------|-------------|
| **Display Width** | 1920 | Width of clip player (pixels) |
| **Display Height** | 1080 | Height of clip player (pixels) |
| **Enable Logging** | False | Log all operations to Streamer.bot log folder |
| **Shoutout Message** | Custom template | Message sent to chat during shoutouts (supports variables) |
| **Featured Clips Only** | False | Limit shoutouts to featured clips only |
| **Max Clip Length** | 30 | Maximum clip duration for shoutouts (seconds) |
| **Clip Max Age** | 30 | Maximum clip age for shoutouts (days) |

**Display Notes:**
- Player automatically maintains 16:9 aspect ratio
- Non-standard dimensions add black bars as needed
- Adjust to match your stream canvas resolution

**Shoutout Message Variables:**
- Use placeholders for dynamic content (configured in Streamer.bot)

### Automatic Shoutouts

The included "Automatic Shoutouts" action triggers `!so` on raid events.

**To Enable:**
1. Ensure the action is enabled in Streamer.bot
2. Verify the raid trigger is active

**To Disable:**
1. Disable the "Automatic Shoutouts" action
2. Manual `!so` commands still work

### Queue Behavior

- **Watch Queue**: Clips from `!watch` and `!replay` play sequentially
- **Shoutout Queue**: Clips from `!so` and raid events play independently
- Queues operate in first-in, first-out (FIFO) order
- `!stop` skips current clip and plays next in queue

## Architecture

### Repository Structure

```
/Cliparino/
├── src/                           # Modern rewrite (canonical)
│   ├── Cliparino.Core/           # Main application (.NET 8)
│   │   ├── Commands/             # Command handlers
│   │   ├── Managers/             # Core managers (Clip, Twitch, OBS, HTTP)
│   │   ├── Models/               # Data models
│   │   ├── Services/             # Background services & health checks
│   │   └── UI/                   # Tray application UI
│   └── tests/                    # Test projects
│       └── Cliparino.Core.Tests/ # Unit tests
├── docs/                          # Planning and tracking documents
│   ├── PLAN.MD                   # Development roadmap & milestones
│   ├── PARITY_CHECKLIST.md       # Feature parity tracking
│   └── MILESTONE_*.md            # Milestone completion docs
├── legacy/                        # Legacy Streamer.bot code (archived)
│   ├── Cliparino/                # Old inline script project
│   ├── FileProcessor/            # Build utilities
│   └── *.ps1, *.cs               # Test scripts
├── Cliparino.sln                 # Root solution file
└── README.md                     # This file
```

### Core Components

The modern rewrite is organized into these key components:

#### **Tray Host**
- Windows system tray application
- Lifecycle management
- Status display and diagnostics export

#### **Local Player Server**
- HTTP server hosting clip player page
- Serves `http://127.0.0.1:<port>/` for OBS Browser Source
- Health endpoint for monitoring

#### **Clip Engine**
- Command parsing and routing
- Clip resolution and metadata
- Queue management (FIFO playback)
- Playback state machine

#### **Twitch Integration**
- OAuth 2.0 authentication with token refresh
- Twitch Helix API client (clips, user data)
- EventSub WebSocket (primary event intake)
- IRC fallback (resilient command intake)
- Chat message sending

#### **OBS Integration**
- obs-websocket client (OBS 28+ built-in server)
- Desired-state enforcement (auto-create/repair scenes & sources)
- Browser source management and refresh
- Drift detection and correction

#### **Health & Self-Repair Supervisor**
- Periodic health checks for all components
- Automatic reconnection with exponential backoff
- Drift correction for OBS configuration
- Bad clip quarantine to prevent queue stalls

### Design Principles

- **"Just Works" Philosophy**: Automatic detection, creation, and repair of resources
- **Self-Healing**: Reconnect on failures, repair drift, quarantine bad clips
- **Modern APIs**: EventSub WebSocket, Helix, obs-websocket (OBS 28+)
- **Graceful Degradation**: Fallback paths (IRC) when modern transports fail
- **Separation of Concerns**: Clean interfaces between components
- **Testability**: Dependency injection and comprehensive unit tests

## Troubleshooting

> **Note**: Since the modern rewrite is still in development, this section will be expanded as the application is tested with end users.

### For Developers

#### Build Issues
- Ensure .NET 8.0 SDK is installed
- Run `dotnet restore` before building
- Check that all project references are correct

#### Test Failures
- Verify all dependencies are installed
- Check test logs for specific error messages
- Ensure external services (Twitch API, OBS) are properly mocked in tests

#### Runtime Issues
- Check application logs in the output directory
- Verify Twitch OAuth tokens are valid
- Ensure OBS WebSocket server is running and accessible
- Confirm firewall isn't blocking localhost HTTP server

### Getting Help

1. **Check Issues**: Search [existing issues](https://github.com/angrmgmt/Cliparino/issues) for solutions
2. **Check Documentation**: Review [`/docs/PLAN.MD`](./docs/PLAN.MD) and milestone completion docs
3. **Enable Debug Logging**: Set logging to verbose for detailed output
4. **Create Issue**: Submit a [new issue](https://github.com/angrmgmt/Cliparino/issues/new) with:
   - .NET version
   - OBS version
   - Steps to reproduce
   - Log excerpts (with tokens redacted)
   - Expected vs. actual behavior

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

### Quick Start for Developers

1. **Clone Repository**
   ```bash
   git clone https://github.com/angrmgmt/Cliparino.git
   cd Cliparino
   ```

2. **Open Solution**
   ```bash
   # Open in Visual Studio, Rider, or VS Code
   Cliparino.sln
   ```

3. **Build Project**
   ```bash
   dotnet build Cliparino.sln
   ```

4. **Run Tests**
   ```bash
   dotnet test Cliparino.sln
   ```

5. **Run Application**
   ```bash
   dotnet run --project src/Cliparino.Core
   ```

### Code Style

- **Language**: C# (latest)
- **Framework**: .NET 8.0 LTS
- **Brace Style**: One True Brace Style (OTB)
- **Documentation**: XML comments for public APIs
- **Naming**: PascalCase for public members, camelCase with `_` prefix for private fields
- **Async**: Prefer async/await for I/O operations

### Legacy Code

The legacy Streamer.bot implementation is archived in [`/legacy/`](./legacy/) for reference only. Development focuses exclusively on the modern rewrite in [`/src/`](./src/).

## Contributing

Contributions are welcome! Please review [CONTRIBUTING.md](./CONTRIBUTING.md) for detailed guidelines.

For major changes, please open an issue first to discuss your proposal.

### Pull Request Templates

- [**Bugfix Request**](https://github.com/angrmgmt/Cliparino/compare?title=Bugfix%20Request&body=%23%23%20Bugfix%20Pull%20Request%0A%0A%23%23%23%20Description%0APlease%20provide%20a%20clear%20and%20concise%20description%20of%20the%20bug%20and%20the%20fix.%0A%0A%23%23%23%20Related%20Issue%0AIf%20applicable%2C%20please%20provide%20a%20link%20to%20the%20related%20issue.%0A%0A%23%23%23%20How%20Has%20This%20Been%20Tested%3F%0APlease%20describe%20the%20tests%20that%20you%20ran%20to%20verify%20your%20changes.%20Provide%20instructions%20so%20we%20can%20reproduce.%0A%0A-%20%5B%20%5D%20Test%20A%0A-%20%5B%20%5D%20Test%20B%0A%0A%23%23%23%20Screenshots%20(if%20appropriate)%3A%0AIf%20applicable%2C%20add%20screenshots%20to%20help%20explain%20your%20problem%20and%20solution.%0A%0A%23%23%23%20Checklist%3A%0A-%20%5B%20%5D%20My%20code%20follows%20the%20style%20guidelines%20of%20this%20project%0A-%20%5B%20%5D%20I%20have%20performed%20a%20self-review%20of%20my%20own%20code%0A-%20%5B%20%5D%20I%20have%20commented%20my%20code%2C%20particularly%20in%20hard-to-understand%20areas%0A-%20%5B%20%5D%20I%20have%20made%20corresponding%20changes%20to%20the%20documentation%0A-%20%5B%20%5D%20My%20changes%20generate%20no%20new%20warnings%0A-%20%5B%20%5D%20I%20have%20added%20tests%20that%20prove%20my%20fix%20is%20effective%20or%20that%20my%20feature%20works%0A-%20%5B%20%5D%20New%20and%20existing%20unit%20tests%20pass%20locally%20with%20my%20changes%0A-%20%5B%20%5D%20Any%20dependent%20changes%20have%20been%20merged%20and%20published%20in%20downstream%20modules)
- [**Feature Request**](https://github.com/angrmgmt/Cliparino/compare?title=New%20Feature%20Request&body=%23%23%20Feature%3A%20%5BFeature%20Name%5D%0A%0A%23%23%23%20Description%0AProvide%20a%20detailed%20description%20of%20the%20feature%20being%20implemented.%20Include%20the%20purpose%20and%20functionality%20of%20the%20feature.%0A%0A%23%23%23%20Related%20Issue%0AIf%20applicable%2C%20mention%20any%20related%20issues%20or%20link%20to%20the%20issue%20number.%0A%0A%23%23%23%20Implementation%20Details%0ADescribe%20how%20the%20feature%20was%20implemented.%20Include%20information%20about%20any%20new%20files%2C%20functions%2C%20or%20changes%20to%20existing%20code.%0A%0A%23%23%23%20Testing%0AExplain%20how%20the%20feature%20was%20tested.%20Include%20details%20about%20any%20unit%20tests%2C%20integration%20tests%2C%20or%20manual%20testing%20performed.%0A%0A%23%23%23%20Checklist%0A-%20%5B%20%5D%20I%20have%20performed%20a%20self-review%20of%20my%20own%20code%0A-%20%5B%20%5D%20I%20have%20commented%20my%20code%2C%20particularly%20in%20hard-to-understand%20areas%0A-%20%5B%20%5D%20I%20have%20made%20corresponding%20changes%20to%20the%20documentation%0A-%20%5B%20%5D%20I%20have%20added%20tests%20that%20prove%20my%20fix%20is%20effective%20or%20that%20my%20feature%20works%0A-%20%5B%20%5D%20New%20and%20existing%20unit%20tests%20pass%20locally%20with%20my%20changes%0A-%20%5B%20%5D%20Any%20dependent%20changes%20have%20been%20merged%20and%20published%20in%20downstream%20modules%0A%0A%23%23%23%20Screenshots%20(if%20applicable)%0AIf%20applicable%2C%20add%20screenshots%20to%20help%20explain%20your%20feature.)

For major changes, please open an issue first to discuss your proposal.

## License

Cliparino is licensed under the [LGPL 2.1](./LICENSE).

Copyright (C) 2024 Scott Mongrain - angrmgmt@gmail.com

## Acknowledgments

- [**Streamer.bot**](https://streamer.bot/) - The automation platform that makes this possible
- [**OBS Studio**](https://obsproject.com/) - For the streaming infrastructure
- [**Twitch**](https://www.twitch.tv/) - For the API and platform

## Links

- **Repository**: [github.com/angrmgmt/Cliparino](https://github.com/angrmgmt/Cliparino)
- **Releases**: [Latest Release](https://github.com/angrmgmt/Cliparino/releases/latest)
- **Issues**: [Report a Bug](https://github.com/angrmgmt/Cliparino/issues/new)
- **Discussions**: [GitHub Discussions](https://github.com/angrmgmt/Cliparino/discussions)
- **Contact**: angrmgmt@gmail.com
