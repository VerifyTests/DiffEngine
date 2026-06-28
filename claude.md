# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution

`src/DiffEngine.slnx`

## Build and Test Commands

```bash
# Build (from repo root)
dotnet build src --configuration Release

# Run all tests
dotnet test --project src/DiffEngine.Tests --configuration Release
dotnet test --project src/DiffEngineTray.Tests --configuration Release
dotnet test --project src/DiffEngineTray.Avalonia.Tests --configuration Release

# Run a single test project with filter
dotnet test --project src/DiffEngine.Tests --configuration Release --filter "FullyQualifiedName~ClassName"

# Run a specific test
dotnet test --project src/DiffEngine.Tests --configuration Release --filter "FullyQualifiedName=DiffEngine.Tests.ClassName.TestMethod"
```

**SDK Requirements:** .NET 10 SDK (see `src/global.json`). The project uses preview/prerelease SDK features.

**Target Frameworks:**
- DiffEngine library: net462, net472, net48, net6.0, net7.0, net8.0, net9.0, net10.0 (Windows also includes .NET Framework targets)
- DiffEngineTray.Core: net10.0 shared, platform-agnostic tray logic
- DiffEngineTray: net10.0 Windows Forms head (Windows only)
- DiffEngineTray.Avalonia: net10.0 Avalonia 12 head (macOS/Linux)
- DiffEngineTray.Launcher: net10.0 cross-platform launcher; the published `DiffEngineTray` dotnet tool
- Tests: net10.0 (DiffEngineTray.Tests is net10.0-windows; net48 also on Windows)

On non-Windows the solution builds with the `Release-NotWindows` configuration, which excludes the
Windows Forms head and its tests (see `src/DiffEngine.slnx`).

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

**DiffEngineTray** - the cross-platform tray utility that handles pending file diffs. It is split into a shared core, two platform UI "heads", and a launcher that bundles them into a single dotnet tool:
- `DiffEngineTray.Core` (`src/DiffEngineTray.Core/`) - shared, platform-agnostic logic. Includes `PiperServer` (localhost TCP server receiving move/delete payloads from the DiffEngine library), `Tracker` (manages pending moves/deletes via concurrent dictionaries), payload models, settings, and file/process utilities. Windows-only P/Invokes are guarded with `OperatingSystem.IsWindows()`. The `TrayServices` static holds platform seams (confirm dialog, open directory, reveal file) that each head wires up at startup.
- `DiffEngineTray` (`src/DiffEngineTray/`) - the Windows Forms head (Windows only). Renders the tray menu via `NotifyIcon`/`MenuBuilder`; owns the registry run-at-login, `RegisterHotKey` global hotkeys, and the options form.
- `DiffEngineTray.Avalonia` (`src/DiffEngineTray.Avalonia/`) - the [Avalonia](https://avaloniaui.net) head (macOS/Linux). Renders a `TrayIcon` + `NativeMenu` (`TrayMenuBuilder`); owns the macOS LaunchAgent run-at-login, Carbon `RegisterEventHotKey` global hotkeys (`MacHotKeys`), and the options window.
- `DiffEngineTray.Launcher` (`src/DiffEngineTray.Launcher/`) - the published `DiffEngineTray` dotnet tool (`PackAsTool`). At runtime it detects the OS and `dotnet exec`s the matching head, which are bundled under `tools/net10.0/any/{windows,avalonia}/` by an MSBuild `TargetsForTfmSpecificContentInPackage` target. The full package (both heads) can only be produced on Windows; pack via `dotnet build` (not `dotnet pack`, which double-packs).

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
- Tests use TUnit and Verify for snapshot testing
