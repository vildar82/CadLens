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
