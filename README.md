# CAD Lens

<img src="src/CadLens.UI/Resources/CadLens.png" alt="CAD Lens icon" width="96">

Explore an AutoCAD drawing by layer, object type, and property. CAD Lens helps you find, select,
focus on, and temporarily isolate objects without changing stored DWG geometry or properties.

**[Download the latest release](https://github.com/vildar82/CadLens/releases)** ·
[Installation](#install-and-run) · [Report an issue](https://github.com/vildar82/CadLens/issues)

## Contents

- [Overview](#overview)
- [Install and run](#install-and-run)
- [Language](#language)
- [Use the Layers lens](#use-the-layers-lens)
- [Use the Object Types lens](#use-the-object-types-lens)
- [Appearance](#appearance)
- [Development](#development)
- [Verification](#verification)

## Overview

- **Layers:** browse Layer → Type → Object and see how the drawing is organized.
- **Object Types:** browse Type → Object across layers, inspect properties, and combine property groups.
- **Drawing controls:** focus the camera, select objects in CAD, or temporarily isolate a target.
- **Object scope:** explore all direct objects in the active space or a captured CAD selection.
- **Preferences:** choose English or Russian, customize appearance, and keep separate settings for each lens.

The panel opens as a compact bar over the drawing. Activate a lens to explore; collapsing or closing
it clears temporary isolation and restores the ordinary display. Drag the empty space in the header
to move the panel.

The current bundle targets **64-bit Windows, AutoCAD 2025–2027, and Civil 3D 2025–2027**, using WPF.
It includes separate payloads selected automatically by the host:

| Host version | AutoCAD series | Bundle payload |
| --- | --- | --- |
| AutoCAD / Civil 3D 2025 | R25.0 | `Contents/net8.0-windows` |
| AutoCAD / Civil 3D 2026 | R25.1 | `Contents/net8.0-windows` |
| AutoCAD / Civil 3D 2027 | R26.0 | `Contents/net10.0-windows` |

The 2025 API is supported by AutoCAD 2026; AutoCAD 2027 requires the 2027 API and .NET 10.
See Autodesk's [managed .NET compatibility table](https://help.autodesk.com/cloudhelp/2027/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm).
These are the intended hosts; native compatibility needs verification in each host/version and update.
AutoCAD 2024 and earlier require a separate .NET Framework backport and are outside this bundle's scope.
AutoCAD LT and macOS are also outside the current release scope.

The screenshots below show the production WPF interface with sample drawing data. They illustrate the
interface and do not establish behavior inside AutoCAD or Civil 3D. See [Verification](#verification).

## Install and run

### Download and install

1. Open the [latest GitHub release](https://github.com/vildar82/CadLens/releases) and download **CadLens.bundle.zip** from **Assets**.
2. Extract the ZIP and copy the complete `CadLens.bundle` directory to `%PROGRAMFILES%\Autodesk\ApplicationPlugins`.
3. Restart AutoCAD or Civil 3D, open a DWG, and run `CADLENS`.
4. Press **Layers** or **Objects** in the compact bar to explore the active drawing space.

The bundle loads the plugin when `CADLENS` is invoked. Releases are currently published as prereleases;
check their notes and the [verification guide](docs/verification.md) for evidence limits.

To update, close the host and replace the entire `CadLens.bundle` directory with the new one. To uninstall,
close the host and remove that directory. Do not replace plugin DLLs while the host is running.

### Manual development load

Install the .NET 8 and .NET 10 SDKs, then build the solution with the .NET 10 SDK:

```powershell
dotnet build CadLens.slnx -c Debug
```

Debug builds copy the plugin and its dependencies to `%APPDATA%\Autodesk\ApplicationPlugins\CadLens.bundle\Contents\<TargetFramework>`. For manual `NETLOAD`, select `CadLens.AutoCAD.dll` from `net8.0-windows` for 2025/2026 or `net10.0-windows` for 2027, then enter `CADLENS`. Library and test project outputs stay in their local build directories. If the build directory is not in `TRUSTEDPATHS`, copy the complete matching output directory, including its `ru` satellite-resource subdirectory, into an existing trusted directory without disabling `SECURELOAD`. Restart the host before loading rebuilt assemblies from a previously loaded plugin.

## Language

CAD Lens starts with the current user's Windows display language: Russian for a Russian display language,
English for English or any unsupported language. This is independent of Windows regional formatting
and the AutoCAD interface language.

Use the EN/RU button in the header to choose **Use Windows language**, **English**, or **Русский**.
The change applies immediately, including tooltips, details, and status messages, while preserving
navigation, inclusion filters, and drawing effects. Layer names, layout names, entity type names, and
handles remain as supplied by the drawing. Third-party exception details remain in their original language.

The preference is saved per Windows user in `%LOCALAPPDATA%\CadLens\language.json`. An absent or
invalid file uses Windows mode; an unwritable file keeps the selected language for the current session
and shows a save explanation in the menu. Windows mode reads the display language at startup and
when selected again. A Windows display-language change usually also requires Windows sign-out.

## Use the Layers lens

A new `CADLENS` session opens as a compact bar with both lenses inactive. Press Layers to read the active drawing space. Browse layers, object types, and objects with the list, breadcrumbs, Back, and Previous/Next. Use Refresh after drawing edits. Press Layers again to collapse the panel; reopen it to restore valid navigation, inclusion filters, and Auto settings without moving the camera. Pending or failed cleanup appears in the compact status tooltip and can delay reactivation. Running `CADLENS` again brings the existing panel forward.

### Object scope

Both lenses start with **All objects**, which reads direct objects in the active model or paper space.
Every click on **Selected objects**, including when already checked, uses the current CAD selection
or asks you to select objects when nothing is selected, then reads them immediately. Press Escape to cancel selection
and keep the previous view. Only live direct objects in the active space are included. The existing
off/frozen inclusion filters still apply.

Navigation, grouping, inclusion-filter changes, Reset, and Select/Auto Select keep the captured inventory.
Collapsing the panel or switching lenses also keeps each lens's selected inventory and navigation.
**Refresh selected objects** also uses the current CAD selection or asks you to select objects, then
updates the list immediately. It can replace the inventory with a selection made by Select/Auto Select.
Switching drawings or spaces clears the old inventory and returns the scope to All objects. Each new
panel session starts with All objects.

### Drawing controls

| Action | Effect | Auto default |
| --- | --- | --- |
| Focus | Fits the current target in the active camera | Off |
| Select | Replaces the CAD selection | Off |
| Isolate | Temporarily hides other direct active-space objects | Off |

Each action has its own Auto toggle. All three default to off; saved choices are restored for each lens.
Enabled actions follow navigation. For selection without camera movement, turn Auto Select on and leave
Auto Focus off. Select remains available when Focus cannot use bounds or the viewport is locked.

### Temporary isolation

Press Isolate on a layer, type, or object to keep its direct active-space objects in their original appearance and temporarily hide the other direct active-space objects. Auto Isolate repeats this as you browse; with Auto Isolate off, the next navigation clears a manual isolation. Objects on off or frozen layers stay hidden even when included in the list. Isolation uses the same target set in every viewport where those objects are visible, without moving cameras or changing CAD selection.

Turning Auto Isolate off, returning to the root list, pressing Reset, collapsing or closing the panel, or switching drawings or spaces restores the ordinary display. Isolation does not write entity or layer visibility properties to the DWG.

Reset also clears CAD selection and turns off all Auto modes while keeping the camera, navigation,
and inclusion filters. It saves the Auto modes as off. Effects stay clear during later navigation until
a mode is enabled again. Reset is disabled while work is pending. Turning Auto Select off clears only
CAD selection. With Auto Select off, a manual selection persists until replaced or cleared.

Layers and Objects save their inclusion filters, search text, sorting column/direction, and Auto modes
independently under `%LOCALAPPDATA%\CadLens`. Closing and reopening the panel or restarting AutoCAD
restores these preferences. The panel still starts collapsed; activate a lens to read the drawing.
Each fresh session starts at the root list, with drawing-specific targets and navigation cleared.
The panel also saves its expanded width and height when collapsed or closed. Activating a lens restores
that size within the current monitor's work area. Window position is not saved.
Restored Auto modes apply when you choose a target. If saving fails, the controls remain usable for
the current session and the lens status explains that the preferences could not be saved.

CAD Lens uses one exploration session for the active drawing. Focus moves only the active view. Switching drawings or spaces reloads the active lens. These actions do not change stored DWG geometry or properties.

## Use the Object Types lens

Press Objects to browse object types across all included layers in the current model or paper space and see their counts. Search the type names or sort by name/count, then browse Type → Object; each object shows its own layer and visibility details. A block insertion counts as one object. Nested block and external-reference contents are not traversed.

![Object Types lens with sample object properties and grouping controls](docs/images/objects-preview.png)

Object Types shares the drawing controls and inclusion filters described above. Switching lenses clears the previous lens's selection/isolation and restores the destination lens's valid navigation and settings. Counts include off-screen objects and stay unchanged when panning or zooming. Use Refresh after drawing edits.

Within a primitive type in either lens, object rows show a useful measurement: vertices for polylines,
direct definition entities for blocks, boundary loops for hatches, control points for splines, length for
lines, radius for circles/arcs, and height for text. Use **Show property** to choose which observed property
appears beside each object and controls value sorting. Numeric values sort numerically; text and assigned
properties use their typed values. Click the value column header to reverse the order; Previous/Next
follows the displayed order. Unavailable values display a dash and sort last. Geometry distances use
drawing units; assigned lineweights use millimeters. The displayed property is saved per lens and type.

Decimal measurements display up to the drawing's `LUPREC` digits, and angles use `AUPREC` in degrees.
Trailing zeros are omitted. Counts remain integers, and assigned lineweights retain their millimeter
precision. Refresh applies changed drawing precision to rows, group captions, details, and tooltips;
grouping and sorting continue to compare the exact stored values.

Use **Group by** to combine properties such as Color + Linetype + Lineweight, or hatch Pattern + Angle.
Each distinct combination creates one subgroup with its own object count and drawing controls. Clear the
checkboxes to return to individual objects. Grouping choices are saved separately per lens and primitive
type. Grouping and sorting use the last inventory; Refresh rereads changed drawing properties.

The dropdown lists the properties observed on objects of the current type, including measurements.

Grouping compares assigned values, preserving ByLayer, ByBlock, indexed colors, true colors, and named
color-book entries. Numeric grouping uses exact stored values, independently of rounded or localized
captions. Lineweight, constant polyline width, and extrusion thickness are separate properties.

A block insertion remains one drawing target. Its structural measurement counts live direct entities in
the referenced definition, including attribute definitions; a nested insertion counts once. Attached
insertion attributes are reported separately. Dynamic blocks use their original name and the referenced
definition's count. This does not promise a visible-geometry count. Xref contents are not counted.

## Appearance

Open the gear button in the compact bar to choose a theme, surface palette, and accent color.
Follow AutoCAD is the default: CAD Lens reads `COLORTHEME` when the panel opens and follows later
host theme changes. Light and Dark override that preference for CAD Lens only. Quiet, Graphite,
and Paper palettes each support both themes, with Mint, Blue, Violet, or Amber accents.

Changes apply immediately to the bar, both lenses, and their controls. Restore defaults returns to
Follow AutoCAD, Quiet, and Mint. Preferences are saved per Windows user in
`%LOCALAPPDATA%\CadLens\appearance.json` and survive closing the panel or host. If the file is damaged,
CAD Lens uses defaults; if it cannot be saved, the choices still work for the current session.
Appearance changes do not change AutoCAD's theme, the drawing, navigation, or temporary effects.

Click **CAD Lens ⓘ** in the compact bar to read about the program, open the GitHub project,
or submit questions and suggestions through GitHub Issues. The links open in your default browser.

## Development

### Build the bundle

From the repository root, run:

```powershell
./scripts/New-Bundle.ps1
```

The script publishes both target frameworks, generates a version-specific `PackageContents.xml`, and creates
`artifacts/bundle/CadLens.bundle.zip`. A successful GitHub Actions run also provides the ZIP as a
build artifact; GitHub Releases provide the versioned download for users.

### Project map

- `Common` contains host results, typed ID contracts, and the object visualization contract without WPF or AutoCAD.
- `Common.AutoCAD` owns queued AutoCAD work, database helpers, temporary visual isolation, Focus, and bounds reading.
- `CadLens.Lenses` groups detached drawing inventories by layer or object type and owns navigation state.
- `CadLens.UI` owns the window, lens switching, and the shared object explorer view and view model.
- `CadLens.AutoCAD` connects the UI to AutoCAD and owns plugin and panel lifetime.

Both lenses use `ObjectExplorerViewModel` and `ObjectExplorerView`. A refresh requests its grouping through `IObjectExplorerActions`; the AutoCAD adapter invokes `DrawingLensProvider`, which reads a detached `DrawingInventory` and builds either Layer → Type → Object or Type → Object groups. Drawing actions use the same `IObjectVisualizationService`. Analysis runs over ordinary .NET models after the inventory is read in the proper document context.

All objects use the same `EntitySnapshot` model with a property dictionary and an optional primary row
measurement. Native extraction lives in `AutoCadEntitySnapshotReader`; `DrawingProperties` provides shared
property access, and `DrawingLensProvider` builds both exploration trees. Details, combined grouping,
and numeric sorting use these same detached values. A new primitive reader does not require a new UI
model or a separate grouping implementation.

Drawing precision is read once with the inventory. `DrawingValueFormatter` applies it at the shared
presentation boundary, keeping raw property values available for grouping and sorting.

### Host and lens lifetime

`CadLens.AutoCAD` owns plugin and panel lifetime, cleanup, snapshot construction, and panel messages. Create `AutoCadTaskService` on the host UI thread. Capture preselection on that thread before queuing interactive selection work. Clear temporary isolation when leaving the drawing context, then stop and drain the queue before disposing it.

A lens implements `ILens` in `CadLens.UI`. It supplies a descriptor and WPF view, handles activation and deactivation, and receives context-change and close notifications. Register its own dependencies and the lens in the composition root:

```csharp
services.AddScoped<MyLensService>();
services.AddScoped<MyLensViewModel>();
services.AddScoped<ILens, MyLens>();
```

The shell discovers `ILens` registrations, creates their views lazily on the UI thread, and shows one active module at a time. Descriptor IDs must be unique. Deactivation must settle module work and remove its effects; failed cleanup blocks switching until a retry succeeds. Context changes invalidate saved targets, and Close cancels work and removes effects before the host queue stops. Module services are scoped to the panel session.

Production registers two `ObjectExplorerLens` instances with different groupings. Each owns its navigation,
filters, search, and Auto settings; both reuse one scoped inventory provider and host-action adapter.
`SettingsService` handles JSON loading and atomic saves for language, appearance, and lens preferences.
Shared WPF files live in `src/CadLens.UI/Lenses/ObjectExplorer`, and drawing models/grouping live in
`src/CadLens.Lenses/Drawing`. Tests register an unrelated Counter lens to check that the shell still
supports other modules.

### CI and releases

Current code and tests describe the supported behavior. Keep documentation for usage, important decisions, and host constraints; ordinary changes do not require separate planning artifacts.

On every push, the Windows GitHub Actions workflow installs the .NET 8 and .NET 10 SDKs, restores dependencies, builds the solution, runs managed tests for both target runtimes, and uploads the bundle ZIP. Native AutoCAD rendering and lifecycle checks are separate.

To publish a prerelease, update `Version` in `Directory.Build.props` and merge it into `main`. After a successful build, the workflow creates the `v<Version>` tag and attaches `CadLens.bundle.zip` to the GitHub Release. Runs with an existing release version leave that release unchanged. You can also start the workflow with **Run workflow** or `gh workflow run build.yml --ref main`.

GitHub generates the release description from merged pull request titles, contributors, and a link to the full commit history. Use descriptive pull request titles for changes users should see in release notes.

## Verification

See the [verification guide](docs/verification.md) for managed checks, native AutoCAD scenarios, and the current evidence limits. Build and test results do not establish native rendering, lifecycle, or host compatibility. Earlier Layers and Highlight acceptance does not verify the current Isolate behavior.
