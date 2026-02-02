# Contributing to Cliparino

Thank you for your interest in contributing to Cliparino! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Pull Request Process](#pull-request-process)
- [FileProcessor Usage](#fileprocessor-usage)

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

### Prerequisites

- **.NET Framework 4.8.1** (or .NET SDK with Framework targeting)
- **Visual Studio 2019+**, **JetBrains Rider**, or **VS Code**
- **Git** for version control
- **Streamer.bot** (v0.2.6+) for testing
- **OBS Studio** for integration testing

### Setup Steps

1. **Clone the repository**
   ```bash
   git clone https://github.com/angrmgmt/Cliparino.git
   cd Cliparino
   ```

2. **Open the solution**
   ```bash
   # Visual Studio / Rider
   start Cliparino.sln
   
   # Or use VS Code with C# extension
   code .
   ```

3. **Restore dependencies**
   Dependencies are referenced from the Streamer.bot installation directory. Ensure Streamer.bot is installed at `%AppData%\Streamer.bot\`.

4. **Build the project**
   ```bash
   dotnet build Cliparino/Cliparino.csproj
   ```

## Project Structure

### Source Code Organization

```
Cliparino/src/
├── CPHInline.cs              # Main entry point, command routing
├── Constants/
│   └── CliparinoConstants.cs # All constants and user messages
├── Managers/
│   ├── ClipManager.cs        # Clip operations, caching, search
│   ├── TwitchApiManager.cs   # Twitch API interactions
│   ├── ObsSceneManager.cs    # OBS scene/source management
│   ├── HttpManager.cs        # Embedded HTTP server
│   └── CPHLogger.cs          # Logging utilities
└── Utilities/
    ├── ValidationHelper.cs    # Input validation methods
    ├── InputProcessor.cs      # Input parsing logic
    ├── ConfigurationManager.cs # Settings retrieval
    ├── ErrorHandler.cs        # Error handling utilities
    ├── RetryHelper.cs         # Retry logic with backoff
    ├── ManagerFactory.cs      # Dependency creation
    └── HttpResponseBuilder.cs # HTTP response builders
```

### Key Files

- **CPHInline.cs**: Main entry point implementing Streamer.bot's `CPHInlineBase`. Contains command routing logic.
- **Manager classes**: Encapsulate domain logic (clips, Twitch, OBS, HTTP)
- **Utility classes**: Provide reusable functionality across the codebase
- **Constants**: Centralized constants to eliminate magic numbers/strings

### Understanding the Workflow

1. User issues command in Twitch chat
2. Streamer.bot invokes `CPHInline.Execute()`
3. Command is routed to appropriate handler method
4. Managers perform operations (fetch clip, configure OBS, serve content)
5. Result is returned to Streamer.bot

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

## FileProcessor Usage

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
