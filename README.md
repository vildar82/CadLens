# CAD Lens

CAD Lens is a personal experiment in interfaces for exploring AutoCAD drawings. The idea is inspired by the lenses in Civilization: the same DWG can be viewed through different visual layers, letting users switch context quickly without changing the drawing itself.

![CAD Lens Layers preview showing the drawing inventory and highlight controls](docs/images/layers-preview.png)

## Why this project exists

The goal is to test whether a game-inspired interface can make an unfamiliar drawing easier and more enjoyable to explore. The first useful outcome is a quick understanding of the DWG's structure; more analysis tools may follow as the project develops.

## Possible user scenario

1. The user opens a DWG and runs `CADLENS`.
2. Lens mode opens with a compact panel over the drawing area.
3. The Layers lens shows the number of objects on each included layer.
4. The user selects a layer in the legend and sees its objects temporarily highlighted without changing object or layer properties in the DWG.
5. Closing lens mode removes the temporary visualization.

This is an example for discussion in OpenSpec Explore, not an approved specification for the first feature.

## Possible directions

- Geometry: visual classification of object types.
- Object Inspector: an object card on hover.
- Blocks: block instances and variations.
- Problems: duplicates, gaps, short segments, and other detectable issues.
- History: changes during the current session, if AutoCAD events provide enough information.
- Later: Selection Lens, radial menu, minimap, game elements, and separate Civil 3D/Revit adapters.

These are directions, not commitments for the first release. For example, a historical creation date for every existing DWG object cannot be promised without checking whether the data exists.

## Technical boundaries of the first version

- Hosts: AutoCAD 2025 and 2026 on Windows. Autodesk lists .NET 8 for their original releases; compatibility with later .NET 10 host updates must be checked separately.
- Language and UI: C# and WPF. HUD placement and highlighting must be tested in AutoCAD before the architecture is fixed.
- Analysis is separate from AutoCAD access: read the required snapshot in the proper document context, then calculate aggregates over ordinary .NET models.
- A lens result describes the desired visualization. Clear temporary effects when switching lenses or documents and when closing lens mode.
- The first slice does not require changing DWG geometry or properties.

## Shared libraries

- `Common` contains host operation results and their composition helpers. It targets .NET 8 without a host API dependency.
- `Common.AutoCAD` contains typed database access, the host task queue, selection highlighting, and temporary graphics. It references `Common` and the AutoCAD API, with WPF dispatcher support for the queue.
- `CadLens.AutoCAD` supplies highlight colors and owns plugin/panel lifetime, cleanup, lens snapshot construction, and panel messages. Neither shared library references a CAD Lens project.

Create `AutoCadTaskService` on the host UI thread. Call selection actions on that thread to capture preselection before queuing work. Clear graphics when leaving the drawing context; stop and drain the queue before disposing it. The extracted graphics implementation remains subject to the native rendering checks described below.

## Spec-driven development

The project uses OpenSpec. The reasons for that choice and a short guide are in [docs/SDD.md](docs/SDD.md). We agree on behavior and verification criteria in a specification before implementing one testable slice, then compare the result with that specification.

## Current status

The first plugin is implemented. The `autocad-bootstrap` change is archived, and the command requirements live in `openspec/specs/autocad-command/spec.md`. The verification record and its limits are below.

## Build and manually load the first plugin

Build from the repository root:

```powershell
dotnet build CadLens.slnx -c Debug
```

The plugin is built at `src/AutoCAD/CadLens.AutoCAD/bin/Debug/net8.0-windows/CadLens.AutoCAD.dll`. Open a DWG in AutoCAD 2025 or 2026. If the build directory is not in `TRUSTEDPATHS`, copy all DLLs from that output directory into an existing trusted directory without disabling `SECURELOAD`. Run `NETLOAD` and select the DLL from that directory. Then enter `CADLENS` on the command line. The command now opens the CAD Lens preview panel. Use Refresh to read the active-space inventory, Highlight selection to test temporary emphasis on objects preselected in the drawing, and Clear highlight to restore appearance. Repeated commands activate the existing panel. The preview must not modify stored drawing geometry or properties.

## Explorer preview status

The `first-layers-lens` change is in progress. The current panel uses CommunityToolkit.Mvvm and a WPF UI theme scoped to the window. It supports inclusion filters, group/type/object navigation, breadcrumbs, Back, and Previous/Next with an object counter. Browsing changes panel content without moving the view. Focus explicitly fits the current group or object using live bounds, with an explanation for unavailable bounds, locked layout viewports, or unsupported views. Hidden objects remain hidden. Opening a layer, type, or object now requests temporary emphasis through the existing graphics preview; Back restores broader targets and All clears the effect. This user-requested trial still needs native graphics and viewport-isolation verification; Highlight selection still operates on objects preselected in the drawing. The user confirmed that Focus now works in the tested case; the full native scenario matrix remains open. See [implementation verification](openspec/changes/first-layers-lens/verification.md) for build/test evidence and the outstanding graphics gate.

## Verification of the first plugin

On September 19, 2026, the plugin was loaded with `NETLOAD` in Autodesk Civil 3D 2026 based on AutoCAD 2026 (`acad.exe`, version 25.1s, .NET 8.0.31). In a new drawing, `CADLENS` printed the greeting used at that time, which contained `CAD Lens`; `DBMOD` was `0` before and after the command. The temporary DLL copy and logging settings used for the check were removed afterward. The greeting text was subsequently changed to English; that wording has not yet been checked in the host.

Separate installations of standard AutoCAD 2025 and 2026 were not found on this machine. Compatibility with those installations has not been verified in the host. The Civil 3D 2026 check was sufficient to finish the first slice.

## GitHub Actions

On every push to any branch, a Windows workflow restores dependencies, builds the solution, and runs `dotnet test`. Core, Lenses, managed host-queue, and UI tests run from `CadLens.slnx`. Native AutoCAD rendering and lifecycle checks are separate.