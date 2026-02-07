# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution

`src/DiffEngine.slnx`

## Build and Test Commands

```bash
# Build (from repo root)
dotnet build src --configuration Release

# Run all tests
dotnet test src --configuration Release

# Run a single test file
dotnet test src/DiffEngine.Tests --filter "FullyQualifiedName~ClassName"

# Run a specific test
dotnet test src/DiffEngine.Tests --filter "FullyQualifiedName=DiffEngine.Tests.ClassName.TestMethod"
```

**SDK Requirements:** .NET 10 SDK (see `src/global.json`). The project uses preview/prerelease SDK features.

**Target Frameworks:**
- DiffEngine library: net462, net472, net48, net6.0, net7.0, net8.0, net9.0, net10.0 (Windows also includes .NET Framework targets)
- DiffEngineTray: net10.0 Windows Forms application
- Tests: net10.0 (net48 on Windows)

## Architecture Overview

DiffEngine is a library that manages launching and cleanup of diff tools for snapshot/approval testing. It's used by ApprovalTests, Shouldly, and Verify.

### Core Components

**DiffEngine Library (`src/DiffEngine/`):**
- `DiffRunner` - Main entry point. Launches diff tools via `Launch`/`LaunchAsync` methods and kills them via `Kill`. Handles process lifecycle.
- `DiffTools` - Registry of available diff tools. Maintains lookups by extension and path. Initialized from `Definitions` and ordered by `OrderReader`.
- `Definitions` - Static collection of all supported diff tool definitions. Each tool is defined in `Implementation/` folder.
- `Definition` - Record type describing a diff tool: executable paths, command arguments, supported extensions, OS support, MDI behavior, auto-refresh capability.
- `DiffTool` - Enum of all supported diff tools (BeyondCompare, P4Merge, VS Code, etc.)
- `ResolvedTool` - A diff tool that was found on the system with its resolved executable path.
- `BuildServerDetector` - Detects CI/build server environments to disable diff tool launching.

**DiffEngineTray (`src/DiffEngineTray/`):**
- Windows Forms tray application that handles pending file diffs
- `PiperServer` - TCP server (localhost) receiving move/delete payloads from DiffEngine library
- `Tracker` - Manages pending file moves and deletes with concurrent dictionaries
- Allows accepting/discarding diffs from system tray

### Adding a New Diff Tool

1. Add enum value to `DiffTool.cs`
2. Create implementation in `src/DiffEngine/Implementation/` following existing patterns (see `BeyondCompare.cs`)
3. Register in `Definitions.cs` collection
4. The `Definition` record specifies:
   - Executable name and search paths per OS (`OsSupport`)
   - Argument builders for temp/target file positioning
   - Binary file extensions supported
   - Whether tool supports auto-refresh, is MDI, requires target file to exist

### Key Patterns

- Tool discovery uses wildcard path matching (`WildcardFileFinder`) to find executables in common install locations
- Tool order can be customized via `DiffEngine_ToolOrder` environment variable
- `DisabledChecker` respects `DiffEngine_Disabled` env var
- Tests use xUnit and Verify for snapshot testing
