# CAD Lens

CAD Lens is a personal experiment in exploring AutoCAD drawings through game-inspired visual layers. Its first lens helps reveal a DWG's structure without changing drawing geometry or properties.

## Contents

- [Overview](#overview)
- [Install and run](#install-and-run)
- [Use the Layers lens](#use-the-layers-lens)
- [Standalone UI preview](#standalone-ui-preview)
- [Development](#development)
- [Verification](#verification)
- [Future ideas](#future-ideas)

## Overview

The current plugin provides one production lens, Layers. It counts objects by layer, lets you browse layer, object type, and individual object, and can focus the camera, select CAD objects, or temporarily isolate the current target. The panel stays over the drawing; closing or collapsing it restores the ordinary display.

CAD Lens uses C#, .NET 8, and WPF on Windows. The intended hosts are AutoCAD 2025 and 2026 and their Civil 3D counterparts. The bundle manifest declares AutoCAD release R25.0 as its minimum; compatibility with newer host or .NET runtime updates needs a separate native check. AutoCAD LT is outside the current scope.

## Install and run

### Bundle

Build the bundle from the repository root:

```powershell
./scripts/New-Bundle.ps1
```

The script publishes the plugin, generates `PackageContents.xml`, and creates `artifacts/bundle/CadLens.bundle.zip`. A successful GitHub Actions run also offers that ZIP as a downloadable artifact.

1. Extract the ZIP.
2. Copy the `CadLens.bundle` directory to `%PROGRAMFILES%\Autodesk\ApplicationPlugins`.
3. Restart AutoCAD or Civil 3D, open a DWG, and run `CADLENS`.

The bundle loads the plugin when `CADLENS` is invoked. To update an installation, close the host and replace the entire `CadLens.bundle` directory.

### Manual development load

Build the solution:

```powershell
dotnet build CadLens.slnx -c Debug
```

The entry assembly is `src/AutoCAD/CadLens.AutoCAD/bin/Debug/net8.0-windows/CadLens.AutoCAD.dll`. If the build directory is not in `TRUSTEDPATHS`, copy all DLLs from that output directory into an existing trusted directory without disabling `SECURELOAD`. In AutoCAD, run `NETLOAD`, select `CadLens.AutoCAD.dll`, and enter `CADLENS`. Restart the host before loading rebuilt assemblies from a previously loaded plugin.

## Use the Layers lens

A new `CADLENS` session opens as a compact bar with Layers inactive. Press Layers to read the active drawing space. Browse layers, object types, and objects with the list, breadcrumbs, Back, and Previous/Next. Use Refresh after drawing edits. Press Layers again to collapse the panel; reopen it to restore valid navigation, inclusion filters, and Auto settings without moving the camera. Pending or failed cleanup appears in the compact status tooltip and can delay reactivation. Running `CADLENS` again brings the existing panel forward.

![CAD Lens Layers preview showing the drawing inventory and Isolate control](docs/images/layers-preview.png)

### Drawing controls

| Action | Effect | Auto default |
| --- | --- | --- |
| Focus | Fits the current target in the active camera | Off |
| Select | Replaces the CAD selection | Off |
| Isolate | Temporarily hides other direct active-space objects | Off |

Each action has its own Auto toggle, and all three start off. Enabled actions follow navigation. For selection without camera movement, turn Auto Select on and leave Auto Focus off. Select remains available when Focus cannot use bounds or the viewport is locked.

### Temporary isolation

Press Isolate on a layer, type, or object to keep its direct active-space objects in their original appearance and temporarily hide the other direct active-space objects. Auto Isolate repeats this as you browse; with Auto Isolate off, the next navigation clears a manual isolation. Objects on off or frozen layers stay hidden even when included in the list. Isolation uses the same target set in every viewport where those objects are visible, without moving cameras or changing CAD selection.

Turning Auto Isolate off, returning to the root list, pressing Reset, collapsing or closing the panel, or switching drawings or spaces restores the ordinary display. Isolation does not write entity or layer visibility properties to the DWG.

Reset also clears CAD selection and turns off all Auto modes while keeping the camera, navigation, and inclusion filters. Effects stay clear during later navigation until a mode is enabled again. Reset is disabled while work is pending. Turning Auto Select off clears only CAD selection. With Auto Select off, a manual selection persists until replaced or cleared. Closing the panel discards session settings.

CAD Lens uses one exploration session for the active drawing. Focus moves only the active view. Switching drawings or spaces reloads the active lens. These actions do not change stored DWG geometry or properties.

## Standalone UI preview

Run the same WPF window with sample data, without AutoCAD:

```powershell
dotnet run --project src/CadLens.Preview
```

The preview starts compact and uses the production Layers layout. It includes sample layers and simulated Focus, Select, and Isolate actions. It does not verify native AutoCAD behavior.

For an interactive comparison of Action rows and Action strip, run:

```powershell
dotnet run --project src/CadLens.Preview -- --modes
```

Use `--capture-modes <directory>` to render comparison scenarios and run the mode checks. The optional `--slow`, `--empty`, and `--error` flags simulate a delayed inventory, no objects, and an unavailable inventory; they can be combined. Restart the preview to change scenarios.

## Development

### Project map

- `Common` contains host results, typed ID contracts, and the object visualization contract without WPF or AutoCAD.
- `Common.AutoCAD` owns queued AutoCAD work, database helpers, temporary visual isolation, Focus, and bounds reading.
- `CadLens.Lenses` turns detached drawing snapshots into Layers groups and navigation state.
- `CadLens.UI` owns the window, lens switching, and Layers WPF module.
- `CadLens.AutoCAD` connects the UI to AutoCAD and owns plugin and panel lifetime.
- `CadLens.Preview` runs the shared UI with sample data.

A Layers refresh travels from `LayersViewModel` through `ILayersActions` to the AutoCAD adapter. The adapter reads a detached snapshot through `LayersLensProvider` and sends visualization requests to `IObjectVisualizationService`. Analysis runs over ordinary .NET models after the host snapshot has been read in the proper document context.

### Host and lens lifetime

`CadLens.AutoCAD` owns plugin and panel lifetime, cleanup, snapshot construction, and panel messages. Create `AutoCadTaskService` on the host UI thread. Capture preselection on that thread before queuing selection work. Clear temporary isolation when leaving the drawing context, then stop and drain the queue before disposing it.

A lens implements `ILens` in `CadLens.UI`. It supplies a descriptor and WPF view, handles activation and deactivation, and receives context-change and close notifications. Register its own dependencies and the lens in the composition root:

```csharp
services.AddScoped<MyLensService>();
services.AddScoped<MyLensViewModel>();
services.AddScoped<ILens, MyLens>();
```

The shell discovers `ILens` registrations, creates their views lazily on the UI thread, and shows one active module at a time. Descriptor IDs must be unique. Deactivation must settle module work and remove its effects; failed cleanup blocks switching until a retry succeeds. Context changes invalidate saved targets, and Close cancels work and removes effects before the host queue stops. Module services are scoped to the panel session.

Only `LayersLens` is registered in production. Its WPF files and `ILayersActions` live in `src/CadLens.UI/Lenses/Layers`; its provider, models, and navigation live in `CadLens.Lenses`. The AutoCAD adapter provides drawing operations. Tests register an unrelated Counter lens to check that the shell can load another module without Layers-specific changes.

### Specifications and CI

The project uses OpenSpec to agree on behavior and verification criteria before implementing a slice. See [the spec-driven development guide](docs/SDD.md) and the [current command specification](openspec/specs/autocad-command/spec.md).

On every push, the Windows GitHub Actions workflow restores dependencies, builds the solution, runs managed tests, and uploads the bundle ZIP. Native AutoCAD rendering and lifecycle checks are separate.

To publish a prerelease, update `Version` in `Directory.Build.props` and merge it into `main`. After a successful build, the workflow creates the `v<Version>` tag and attaches `CadLens.bundle.zip` to the GitHub Release. Runs with an existing release version leave that release unchanged. You can also start the workflow with **Run workflow** or `gh workflow run build.yml --ref main`.

GitHub generates the release description from merged pull request titles, contributors, and a link to the full commit history. Use descriptive pull request titles for changes users should see in release notes.

## Verification

The first plugin is implemented, and its bootstrap, Layers, panel, and navigation changes are archived. Their records distinguish managed checks, UI preview checks, and native host observations:

- [First Layers lens verification](openspec/changes/archive/2026-09-20-first-layers-lens/verification.md)
- [Panel and lens switching verification](openspec/changes/archive/2026-09-26-lens-panel-and-switching/verification.md)
- [Independent navigation modes verification](openspec/changes/archive/2026-09-26-independent-navigation-modes/verification.md)

The current Isolate implementation is described in the active [isolation design](openspec/changes/improve-entity-highlighting/design.md). The archived rendering checks above concern the previous Highlight behavior and do not verify Isolate. A diagnostic host probe showed a hatch and block insertion could be hidden with the visibility flag; broader isolation behavior and cleanup still need native checks.

The original bootstrap DLL was loaded with `NETLOAD` in Civil 3D 2026 on September 19, 2026; that check used an earlier greeting command. Before the isolation change, the user reported that the panel and navigation worked in AutoCAD, but the host version and individual scenario results were not recorded. Separate standard AutoCAD 2025 and 2026 installations, and loading the new bundle in a native host, have not been verified here. Managed builds, tests, and preview behavior do not establish those host results.

## Future ideas

Possible later lenses include geometry classification, object inspection, blocks, drawing problems, and session history where source events support it. Selection Lens, a radial menu, minimap, game elements, and separate adapters are also ideas. These are exploration topics, not committed features; existing DWG objects do not necessarily carry a recoverable creation date.
