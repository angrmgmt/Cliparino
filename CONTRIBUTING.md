# Contributing to Cliparino

Thank you for your interest in contributing to Cliparino!

> **⚠️ Note**: This document primarily describes the **legacy Streamer.bot-based version** (archived in [`/legacy/`](./legacy/)). The **modern rewrite** in [`/src/`](./src/) uses .NET 8.0, ASP.NET Core, and a different architecture. For modern codebase structure, see the [README](./README.md#repository-structure). This document will be updated as the modern version nears public release.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Pull Request Process](#pull-request-process)
- [Legacy: FileProcessor Usage](#fileprocessor-usage)

## Code of Conduct

This project adheres to the [Contributor Covenant Code of Conduct](./CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to angrmgmt@gmail.com.

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check the [issue tracker](https://github.com/angrmgmt/Cliparino/issues) to avoid duplicates. When creating a bug report, include:

- **Clear title and description**
- **Steps to reproduce** the issue
- **Expected vs. actual behavior**
- **Streamer.bot version** and **OBS version**
- **Log excerpts** (with tokens redacted)
- **Screenshots or videos** if applicable

### Suggesting Features

Feature suggestions are welcome! Please:

- **Open an issue** with a clear title and description
- **Explain the use case** and how it benefits users
- **Consider implementation complexity**
- **Discuss alternatives** you've considered

### Pull Requests

Pull requests are welcome! For major changes:

1. **Open an issue first** to discuss the proposed changes
2. **Fork the repository** and create a feature branch
3. **Follow coding standards** outlined below
4. **Test thoroughly** before submitting
5. **Update documentation** as needed

## Development Setup

### Modern Codebase (Current)

**Prerequisites**: .NET 8.0 SDK, Windows 10+, OBS Studio 28+

```bash
# Clone and build
git clone https://github.com/angrmgmt/Cliparino.git
cd Cliparino
dotnet build Cliparino.sln

# Run tests
dotnet test Cliparino.sln

# Run application
dotnet run --project src/Cliparino.Core
```

**IDE Options**: Visual Studio 2022+, JetBrains Rider, or VS Code with C# Dev Kit

### Legacy Codebase (Archived)

The legacy Streamer.bot version requires:
- **.NET Framework 4.8.1** (or .NET SDK with Framework targeting)
- **Streamer.bot** (v0.2.6+) installed at `%AppData%\Streamer.bot\`
- **OBS Studio**

Build legacy:
```bash
dotnet build legacy/Cliparino/Cliparino.csproj
```

## Project Structure

### Modern Codebase (`/src/`)

```
src/Cliparino.Core/
├── Controllers/          # HTTP API controllers (Auth, Player, Health, etc.)
├── Models/               # Data models (ClipData, ChatCommand, TwitchEvent, etc.)
├── Services/             # Core services & background workers
│   ├── PlaybackEngine.cs         # Playback state machine & queue processor
│   ├── TwitchHelixClient.cs      # Twitch API client
│   ├── ObsController.cs          # OBS automation & drift repair
│   ├── TwitchEventCoordinator.cs # Event ingestion (EventSub + IRC fallback)
│   └── ...
├── UI/                   # System tray application
├── wwwroot/              # Static web files (player page)
├── Program.cs            # Application entry point
└── appsettings.json      # Configuration template
```

**Architecture**: ASP.NET Core web host + WinForms tray UI, dependency injection, background services

### Legacy Codebase (`/legacy/Cliparino/src/`)

```
legacy/Cliparino/src/
├── CPHInline.cs              # Main entry point (Streamer.bot inline script)
├── Constants/
│   └── CliparinoConstants.cs # Constants and user messages
├── Managers/                 # Domain logic managers
└── Utilities/                # Helper classes
```

**Architecture**: Streamer.bot inline script (.NET Framework 4.7.2)

## Coding Standards

### C# Style Guide

#### Naming Conventions

- **Classes, Methods, Properties**: `PascalCase`
- **Private Fields**: `_camelCase` (leading underscore)
- **Local Variables, Parameters**: `camelCase`
- **Constants**: `PascalCase`

```csharp
public class ClipManager {
    private readonly IInlineInvokeProxy _cph;
    private string _lastClipUrl;
    
    public async Task<ClipData> GetClipDataAsync(string url) {
        var clipId = ExtractClipId(url);
        return await FetchClipAsync(clipId);
    }
}
```

#### Brace Style

Use **One True Brace Style (OTB)**: Opening brace on same line.

```csharp
public bool Execute() {
    if (condition) {
        DoSomething();
    } else {
        DoSomethingElse();
    }
}
```

#### Documentation

- **Public APIs**: Require XML documentation comments
- **Complex Logic**: Add inline comments explaining "why", not "what"
- **Magic Numbers**: Replace with named constants

```csharp
/// <summary>
///     Retrieves clip data from the Twitch API.
/// </summary>
/// <param name="url">The Twitch clip URL.</param>
/// <returns>ClipData object or null if not found.</returns>
public async Task<ClipData> GetClipDataAsync(string url) {
    // Implementation
}
```

### Design Principles

- **DRY (Don't Repeat Yourself)**: Extract common logic into utilities
- **Single Responsibility**: Each class/method has one clear purpose
- **Fail-Safe Defaults**: Provide sensible defaults, validate inputs
- **Error Handling**: Use try-catch with proper logging, never swallow exceptions

### Constants and Configuration

- Add new constants to `CliparinoConstants`
- Group related constants in nested classes
- Use `ConfigurationManager` for retrieving settings from Streamer.bot

```csharp
public static class CliparinoConstants {
    public static class Http {
        public const int BasePort = 8080;
        public const int MaxPortRetries = 10;
    }
}
```

## Testing Guidelines

### Manual Testing

Since Cliparino integrates with Streamer.bot and OBS, manual testing is essential:

1. **Build the project**
   ```bash
   dotnet build Cliparino/Cliparino.csproj
   ```

2. **Generate Streamer.bot-compatible file**
   ```bash
   dotnet run --project FileProcessor/FileProcessor.csproj
   ```

3. **Import into Streamer.bot**
   - Copy contents of `Output/Cliparino_Clean.cs`
   - Import into Streamer.bot as inline code

4. **Test each command**
   - `!watch <url>`
   - `!watch @username search terms`
   - `!so username`
   - `!replay`
   - `!stop`

5. **Verify OBS integration**
   - Check scene creation
   - Verify browser source configuration
   - Test clip playback

### Test Checklist

- [ ] All commands execute without errors
- [ ] Clips play correctly in OBS
- [ ] Queue behavior works as expected
- [ ] Moderator approval works (search)
- [ ] Shoutouts trigger correctly
- [ ] Error messages are user-friendly
- [ ] Logging output is meaningful

## Pull Request Process

### Before Submitting

1. **Sync with main branch**
   ```bash
   git fetch origin
   git rebase origin/main
   ```

2. **Build successfully**
   ```bash
   dotnet build Cliparino/Cliparino.csproj
   ```

3. **Test thoroughly** using manual testing checklist

4. **Update documentation** if you've changed:
   - Public APIs
   - User-facing features
   - Configuration options

5. **Generate clean file** for Streamer.bot
   ```bash
   dotnet run --project FileProcessor/FileProcessor.csproj
   ```

### Pull Request Templates

Use the appropriate template for your PR:

- [**Bugfix Request**](https://github.com/angrmgmt/Cliparino/compare?title=Bugfix%20Request)
- [**Feature Request**](https://github.com/angrmgmt/Cliparino/compare?title=New%20Feature%20Request)

### PR Description Should Include

- **Summary**: Brief description of changes
- **Motivation**: Why is this change needed?
- **Implementation**: How did you implement it?
- **Testing**: How was it tested?
- **Breaking Changes**: Any backward compatibility issues?
- **Related Issues**: Link to related issue(s)

### Review Process

1. Maintainers will review your PR
2. Address any feedback or requested changes
3. Once approved, your PR will be merged
4. Your contribution will be included in the next release!

## Legacy: FileProcessor Usage

> **Note**: FileProcessor is only needed for the legacy Streamer.bot version.

The **FileProcessor** project prepares C# source code for Streamer.bot compatibility by:

- Consolidating multiple files into a single file
- Removing XML documentation comments
- Removing `#region` directives
- Removing pragma directives
- Cleaning up extra whitespace

### Running FileProcessor

```bash
# From repository root
dotnet run --project FileProcessor/FileProcessor.csproj
```

**Output**: `Output/Cliparino_Clean.cs`

### How It Works

The `FileCleaner` class:

1. Reads all `.cs` files from `Cliparino/src/`
2. Extracts and consolidates `using` statements
3. Removes comments and regions
4. Combines all classes into a single file
5. Outputs to `Output/Cliparino_Clean.cs`

**Important**: Always run FileProcessor before importing into Streamer.bot. The raw source files contain documentation that Streamer.bot doesn't support.

### Automation Tip

You can create a build script to automate this:

**build.bat** (Windows)
```batch
@echo off
echo Building Cliparino...
dotnet build Cliparino/Cliparino.csproj
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo Generating Streamer.bot file...
dotnet run --project FileProcessor/FileProcessor.csproj
echo Done! Import Output/Cliparino_Clean.cs into Streamer.bot
```

## Questions?

If you have questions about contributing:

- **Open an issue** for clarification
- **Email**: angrmgmt@gmail.com
- **Check existing issues** for similar questions

Thank you for contributing to Cliparino! 🎉
