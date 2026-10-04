# Verification

## Managed checks

Install the .NET 8 and .NET 10 SDKs and the .NET Framework 4.8 or 4.8.1 runtime.
The .NET Framework reference assemblies restore through NuGet. Run the existing checks relevant to the change.
For solution-wide behavior or composition changes:

```powershell
dotnet build CadLens.slnx -c Release
dotnet test CadLens.slnx -c Release --no-build --no-restore
```

Build with zero warnings and check Rider warnings separately. Managed tests cover detached models,
UI orchestration, and host helpers using stubs; they do not prove native rendering or lifecycle behavior.
All managed test projects run for `net47`, `net48`, .NET 8, and .NET 10; UI and host-stub tests use
Windows targets for .NET 8 and .NET 10. Legacy tests on a newer .NET Framework 4.x runtime do not
establish compatibility with the minimum 4.7/4.8 runtime or an installed CAD host.

Build a fresh bundle and check its legacy dependencies without application configuration files:

```powershell
./scripts/New-Bundle.ps1
./scripts/Test-LegacyDependencies.ps1
```

The check loads both published legacy payloads on .NET Framework and exercises JSON/settings,
MVVM commands, DI async disposal, immutable collections, and dependency resolver scope. It is a managed
loading check; AutoCAD/Civil 3D loading and behavior still require native validation.

## Native evidence limits

Earlier user acceptance covered Layers navigation and the former Highlight behavior without a recorded
host version or detailed scenario matrix. A later diagnostic probe showed that a direct hatch and block
insertion could be hidden by the visibility flag. Broader Isolate behavior, cleanup, and bundle loading
still need native checks. Do not transfer earlier Highlight results to Isolate or assume a running host
has loaded rebuilt DLLs. Native AutoCAD/Civil 3D checks belong to the user.

## Host compatibility

Build a fresh bundle with `./scripts/New-Bundle.ps1`. In fresh AutoCAD and Civil 3D sessions for each
intended version from 2019 through 2027, check command-triggered bundle loading and manual `NETLOAD`
with the matching payload: `Contents/net47` for R23.0–R23.1, `Contents/net48` for R24.0–R24.3,
`Contents/net8.0-windows` for R25.0–R25.1, and `Contents/net10.0-windows` for R26.0.
Legacy hosts require .NET Framework 4.7 (2019–2020) or 4.8 (2021–2024), or a compatible later 4.x runtime.
Run `CADLENS`, activate both lenses, try Focus/Select/Isolate, close the panel, and switch drawings.
Record the exact host product, version/update, bundle version, loaded DLL path, and observed outcomes.

Include AutoCAD 2026.1.2 and the corresponding Civil 3D configuration using the same .NET 8 payload.
Autodesk's [.NET 10 update notice](https://blog.autodesk.io/autodesk-desktop-products-2025-2026-net-10-updates/)
describes a runtime change without API changes; existing plug-ins are expected to continue working
unless affected by .NET 10 breaking changes. Check the exact installed update separately; managed
tests and the bundle manifest do not establish compatibility in that host.

## Language

Switch English/Russian/Use Windows language in compact and expanded states. Check both lenses,
filters, search, object details, status messages, tooltips, and keyboard access at narrow widths.
Changing language while Auto modes or isolation are active must preserve navigation, selection,
camera, and isolation. Drawing layer/type/layout names and handles must remain unchanged.
Reopen CAD Lens and restart the host to confirm persistence. Check Windows display-language
detection with a different regional format and AutoCAD language; unsupported Windows languages
should use English. Test a fresh bundle and a manual NETLOAD copy including the ru subdirectory.

## Native scenarios

Load the rebuilt plugin in a fresh host session and record the host/version and relevant outcomes:

1. Load the bundle, run CADLENS with preselected objects, and confirm compact startup preserves them.
   Activate Layers, browse layer/type/object, Back, breadcrumbs, and Previous/Next. Repeated CADLENS
   should bring the existing panel forward. Activate Object Types and browse Type → Object across layers;
   check per-object layer details, search, count sorting, inclusion filters, and switching between lenses.
   Each lens should retain its own valid navigation and Auto settings. Drawing edits should require Refresh.
   In both lenses, choose Selected objects with CAD preselection and confirm the subset loads immediately.
   While Selected objects remains checked, click it again with a different CAD preselection and confirm
   the subset changes. Clear CAD selection and click it again: CAD should prompt and load the completed
   selection immediately. Check the same two paths through Refresh selected objects. Press Escape during the
   prompt and confirm the previous scope, navigation, and effects stay unchanged. Collapse/switch lenses
   and reopen to retain the selected inventory; switch drawing/layout to reset scope to All objects.
   Change drawing/layout or close the panel during selection and confirm no late result returns.
2. Try Focus, Select, and Isolate independently. With no saved preferences, all Auto modes start off.
   Saved Auto modes should apply only after choosing a target. Auto Select should follow
   navigation without moving the camera; Select should remain usable when a locked viewport blocks Focus.
3. Isolate patterned/solid hatches, text, blocks with attributes/nested blocks, repeated insertions,
   xrefs, and custom/proxy entities. Targets should retain their original appearance; other direct
   active-space objects should disappear. Check selection, object snaps, and plotting while isolated.
4. Include off/frozen layers, use model/paper space and multiple viewports, and confirm native hidden
   states remain hidden. Isolation shares one target set across views; Focus moves only the active view.
5. Reset, navigate to the root, disable Auto Isolate, collapse/reopen, close during pending work,
   switch drawings/spaces, and close the last drawing. Effects must clear without stale work returning;
   reopening should restore valid navigation/settings without moving the camera. Retry failed cleanup.
6. Check keyboard use, narrow layouts, dragging/resizing, mixed-DPI monitors, and a large drawing.
   Compare geometry, entity/layer properties, cameras, and DBMOD before and after temporary effects.
7. Open Appearance in the compact bar. With Follow AutoCAD selected, change `COLORTHEME` between
   0 (Dark) and 1 (Light), then verify both lenses, search selection, menus, tooltips, dropdowns,
   disabled controls, hover, and keyboard focus. Explicit Light/Dark should ignore later host theme
   changes. Try all palettes and accents, including switching while the appearance popup is open.
   Reopen the panel and restart the host to confirm preferences persist; Restore defaults should
   return to Follow AutoCAD, Quiet, and Mint. Theme changes must preserve camera, selection, isolation,
   active navigation, and drawing properties. Managed WPF renders do not verify native host styling.
8. Set different filters, search text, sorting directions, and Auto modes in Layers and Objects.
   Close/reopen the panel and restart the host. Confirm compact startup preserves preselection,
   each lens restores its own controls on activation, and the first inventory uses its saved filters.
   Fresh sessions must start at the root list without old drawing targets or effects. Switch drawings
   and spaces to confirm preferences survive while navigation resets. Press Reset and reopen again:
   Auto modes should stay off while filters, search, and sorting remain saved.
9. Browse polylines of all three families, repeated/dynamic/attributed blocks, hatches (patterned, solid,
   and gradient), splines, lines, circles/arcs, text, and custom objects. Confirm row measurements,
   missing values, named block details, and the separation of lineweight, width, and extrusion thickness.
   Count nested block insertions once, attribute definitions in the definition count, insertion attributes
   separately, and show unavailable counts for xrefs. These are structural counts, not visibility counts.
   Choose different numeric and text fields with Show property, check row values, sort in both directions,
   and check Previous/Next, including changing the property while an object is open. Combine Color,
   Linetype, and Lineweight or hatch Pattern and Angle. Compare each subgroup's Select/Isolate targets
   with its objects. Preserve a selected object when regrouping or refreshing changed properties.
   Check changed subgroup targets with Auto modes enabled. Reopen both lenses to verify their independent per-type grouping choices.
   Switch language, test narrow widths and long names, and confirm grouping stays unchanged. Refresh
   after editing properties; grouping and sorting alone must not reread the drawing.
   Change `LUPREC` and `AUPREC`, then Refresh. Check rounded rows, group captions, details, and tooltips
   in English and Russian. Values that round to the same caption should keep their distinct groups
   and exact numeric sort order. Check themed grouping checkboxes and centered labels in Light/Dark.
10. In both lenses, filter a primitive type by length, vertex count, closed state, text, and assigned color.
    Apply and clear conditions while viewing a type, subgroup, and individual object. Check that counts
    and Focus/Select/Isolate use exactly the matching targets. Test zero matches with all Auto modes on:
    selection and isolation must clear, the camera must stay unchanged, and the filter must remain editable.
    Invalid input must preserve the previous result and effects. Combine a filter with property grouping,
    off/frozen inclusion, and selected-object scope. Check refresh after edits, repeated selected-scope
    capture, independent filters in each lens, and reset on drawing/layout changes. Check English/Russian
    decimal input, degree input, popup controls, keyboard access, and minimum panel width in both themes.
11. Open ordinary and dynamic block insertions with attached attributes in both lenses. Compare repeated
    insertions of the same definition with different values. Check exact tags, blank and unavailable values,
    long tags and multiline text at minimum panel width in English and Russian. Blocks without attributes
    should show an empty state. Edit a value in CAD: browsing keeps the captured value until Refresh.
    Focus/Select/Isolate must still target the whole insertion. Check that browsing does not change DBMOD;
    attribute-definition counts remain separate and no nested block or xref attributes are traversed.
12. Check Area on closed and open planar curves, ellipses, splines, 2D/3D polylines, and hatches.
    Compare values with the host's native area, including unsupported/nonplanar cases that should show
    unavailable values. Area is in square drawing units and uses drawing precision. In both lenses,
    display, group, filter, and save an area condition; reuse it in another drawing, then edit geometry
    and Refresh. Check that ordinary length metrics and Focus/Select/Isolate targets remain correct.
13. Use attributed dynamic block insertions with different marks, visibility states, distances, angles,
    and flip values. Choose prefixed Attribute and Dynamic block properties in both lenses; compare
    displayed values, grouping membership, sorting, filters, and saved presets with each insertion.
    Include a tag and dynamic property named Layer to confirm they remain separate from the built-in
    property. Check blank/missing/duplicate tags, numeric-looking text, and properties with the same
    name but different native value types across definitions. Confirm English/Russian prefixes preserve
    the original names, saved choices survive restart, and Refresh captures edits. Focus/Select/Isolate
    must still target whole insertions, and browsing must not change DBMOD.
