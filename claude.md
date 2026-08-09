# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution

`src/DiffEngine.slnx`

## Build and Test Commands

```bash
# Build (from repo root). Also packs: ProjectDefaults sets GeneratePackageOnBuild in Release.
dotnet build src --configuration Release

# Run all tests
dotnet test --project src/DiffEngine.Tests --configuration Release
dotnet test --project src/DiffEngineTray.Tests --configuration Release
dotnet test --project src/DiffEngineViewer.Tests --configuration Release

# Run a single test project with filter
dotnet test --project src/DiffEngine.Tests --configuration Release --filter "FullyQualifiedName~ClassName"

# Run a specific test
dotnet test --project src/DiffEngine.Tests --configuration Release --filter "FullyQualifiedName=DiffEngine.Tests.ClassName.TestMethod"
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

**DiffEngineViewer (`src/DiffEngineViewer/`):**
- Cross platform GUI diff tool: Dear ImGui rendered through raylib. Reviews inline snapshots and
  plain two-file diffs.
- Bundled inside DiffEngine.nupkg under `tools/viewer/{rid}/`, so inline snapshots work with no
  extra install. Also shipped standalone as the `DiffEngineViewer` dotnet tool.
- `ViewerSession` is a pure state machine over an immutable `SessionState`. `ScreenBuilder`
  projects that into a `Screen` (already sliced to the visible rows), which `AsciiRenderer` draws
  as text and the native shim draws as pixels. Both renderers consume the identical structure,
  which is what makes the text snapshots meaningful.
- Does **not** reference DiffEngine. It links `Inline/*.cs` and `Tray/TrayDetector.cs` as source,
  because DiffEngine publishes and embeds the viewer and a reference back would be a cycle.
- Single instance by socket bind on 3493 (`DiffEngine_ViewerPort`): whoever binds owns the window,
  and a process that fails to bind forwards its patch and exits.

**Native shim (`native/`):**
- `raylib` and `imgui` are fetched by CMake (`FetchContent`), pinned by tag in
  `native/CMakeLists.txt`. Deliberately not submodules: nothing in a normal `dotnet build` touches
  this folder, so a recursive clone on every checkout would serve a path almost nobody takes.
- Building it needs CMake 3.24+, a C++17 compiler and network access. Contributors do not need
  any of that, because the binaries are committed.
- `native/src/deview.cpp` is a renderer for the `Screen` model, not an ImGui binding: ~12 exports
  taking one flat blittable frame description. The ABI is `native/include/deview.h`; bump
  `DEVIEW_VERSION` whenever the structs change.
- Built binaries are **committed** to `src/DiffEngineViewer/runtimes/{rid}/native/`, so a plain
  `dotnet build` produces a shippable package and contributors never need CMake. Regenerate them
  with the `build-native` GitHub workflow, which opens a PR.

**DiffEngineTray (`src/DiffEngineTray/`):**
- Windows Forms tray application that handles pending file diffs
- `PiperServer` - TCP server (localhost) receiving move/delete payloads from DiffEngine library
- `Tracker` - Manages pending file moves and deletes with concurrent dictionaries
- `InlineViewerProxy` - Pending inline snapshots are **not** stored here. The viewer owns that
  queue and the tray drives it over the same socket, so one queue and one set of semantics serve
  every platform rather than a Windows-only copy that can drift.
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
- Tests use TUnit and Verify for snapshot testing
