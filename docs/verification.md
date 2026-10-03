# Verification

## Managed checks

Run the existing checks relevant to the change. For solution-wide behavior or composition changes:

```powershell
dotnet build CadLens.slnx -c Release
dotnet test CadLens.slnx -c Release --no-build --no-restore
```

Build with zero warnings and check Rider warnings separately. Managed tests cover detached models,
UI orchestration, and host helpers using stubs; they do not prove native rendering or lifecycle behavior.

## Native evidence limits

Earlier user acceptance covered Layers navigation and the former Highlight behavior without a recorded
host version or detailed scenario matrix. A later diagnostic probe showed that a direct hatch and block
insertion could be hidden by the visibility flag. Broader Isolate behavior, cleanup, and bundle loading
still need native checks. Do not transfer earlier Highlight results to Isolate or assume a running host
has loaded rebuilt DLLs. Native AutoCAD/Civil 3D checks belong to the user.

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
2. Try Focus, Select, and Isolate independently. All Auto modes start off. Auto Select should follow
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
