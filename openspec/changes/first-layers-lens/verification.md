# Implementation verification

## Current preview

`CADLENS` opens or activates the single modeless panel. `CADLENSVERIFY` has been removed at the user's request. Refresh reads the active-space inventory; Highlight selection captures the drawing's preselection before queuing work, then revalidates the objects before applying the rendering candidate. Clear highlight removes the owned effect without moving the camera.

The panel now uses CommunityToolkit.Mvvm 8.4.0, locally scoped WPF UI resources, a compact inventory, explicit busy/error feedback, and a clearer visual hierarchy. This is still a preview: inclusion toggles, drill-down, breadcrumbs and object browsing are connected. Explicit Focus and navigation emphasis are connected for the user-requested preview trial; native graphics acceptance remains open. See the latest Focus implementation record below for verification limits. The complete first-layers-lens change remains open.

The greeting was removed in a concurrent local edit. That edit is preserved. The existing greeting scenario is therefore not marked verified or complete; the main specification has not been changed.

## Lifetime behavior

- Closing the panel stops requests, detaches session events, removes temporary rendering, waits asynchronously for every admitted request to leave, and disposes the explorer scope. The root provider remains available for a fresh panel session.
- Termination stops requests and removes the overrule without document-view regeneration. It does not depend on another AutoCAD Idle event. Scope and root disposal follow request draining; the UI thread is never blocked waiting for a native callback. Asynchronous disposal completion during host shutdown has not been verified in AutoCAD.
- `IHostTaskService.RunAsync` is implemented by one `AutoCadTaskService`: a FIFO queue, AutoCAD dispatcher/application-context execution, and a document lock. `ContextIdentity`, tokens/generations, the separate context adapter, and snapshot revisions have been removed at the user's request. The service checks the active document; the view model rejects late results after its presentation is reset.

## Shared-library extraction

`Common` now owns `HostResult<T>` and its composition extensions. `Common.AutoCAD` owns the typed database extensions, the existing single task queue, selection highlight actions, and the temporary graphics service. CAD Lens supplies the color options, retains panel messages and lens loading, and continues to own context cleanup and queue shutdown. Neither shared library depends on CAD Lens.

The Debug solution builds with zero compiler/analyzer warnings and errors. All 28 existing tests pass using the extracted code. Validation used `--artifacts-path C:\dev\temp\CadLens-validation\common-extraction` because the normal output encountered a DLL lock. JetBrains InspectCode 2026.2.2 reports no warnings in the new common projects; its six existing solution warnings listed below remain unchanged. Native AutoCAD checks were not rerun for this extraction.

## Automated evidence

- Readability refactor: Debug solution build passed with zero warnings and errors. Release was not rerun for this refactor.
- Readability refactor: 28 Debug tests passed, none skipped (Core 7, Lenses 5, host task/extension tests 11, UI 5). Result extension checks cover unavailable propagation, continuation rejection, and outcome selection.
- Queue tests use a real WPF dispatcher and stub native callbacks to check FIFO execution, document locking, busy/idle handling, changed-document rejection, cancellation, shutdown draining, and callback/scheduling failures. Typed-opening tests check access flags, erased/invalid IDs, and table/block filtering.
- UI tests cover command gating, cancellation, late results after close/context changes, failure feedback, and standalone WPF rendering with long names and 24 groups at 370 x 660 and 300 x 450 DIPs.
- Rendered images were inspected. The smaller layout initially left no inventory space; the compact header correction leaves a scrollable row while keeping the actions visible.
- The plugin output contains Core, Lenses, UI, CommunityToolkit.Mvvm, DI and WPF UI assemblies. No AutoCAD host assemblies are copied beside the plugin.
- JetBrains InspectCode 2026.2.2 completed after the simplification. Six pre-existing warnings remain: four unused presentation/filter properties, one unresolved XAML resource, and one explicit disposal in a UI lifetime test. The solution is not Rider-warning-free.

The host tests compile the actual single task service and object-opening extensions as linked sources against small AutoCAD API stubs. They intentionally do not load AutoCAD assemblies and cannot prove native scheduler, graphics, or document lifecycle behavior.

## Host evidence and remaining checks

The user reported that the initial modeless window opened but lacked visual polish, and that applying emphasis to selected objects produced no visible effect. The selection-capture timing and unconditional Idle wait have been corrected in code; the cause and resulting graphics behavior still require a live host check.

No native graphics verification was performed in this revision. Blocks, text, explicit/ByLayer/ByBlock colors, hidden objects, adjacent viewport isolation, document transitions, DBMOD/property comparisons, shutdown, and other-plugin overrule coexistence remain unverified. The traits-based candidate is not certified as satisfying the dimming specification. In particular, it has no per-viewport rendering filter yet. Full explorer integration remains gated on the checks in tasks 2.3–2.5.

After restarting the host, load the complete rebuilt output and run `CADLENS`. Select direct active-space objects, click Highlight selection, inspect the result message and drawing, then use Clear highlight and close/reopen the panel. Record the exact host version and drawing fixtures before marking host tasks complete. Standard AutoCAD 2025/2026 evidence must remain separate from Civil 3D evidence.

## Highlight freeze investigation

The user reports an unacceptable freeze immediately after Highlight selection, including without an attached debugger. The measurements below narrowed the delay to the active overrule and confirmed improvement after replacing its applicability filter.

Inventory collection now reads active-space ObjectIds directly instead of opening every entity, and its transaction is disposed before applying graphics. This removes unnecessary native object access; it does not establish that regeneration is fast.

Temporary instrumentation measured inventory, registration, regeneration, and SetAttributes callback costs. It has now been removed, including its option, counters, timers, and command-line logging. Historical measurements are retained below.

### First user measurement

The user measured 4,389 active-space identifiers and 20 targets: inventory 3.1 ms, overrule registration 4.9 ms, and regeneration 21,109.8 ms. There were 4,402 SetAttributes calls, with 1.6 ms accumulated in the base implementation and 4.6 ms in the color override. The measured callback bodies do not explain the delay. The remaining regeneration work, including geometry generation and graphics-system/overrule overhead outside those bodies, has not yet been isolated. A normal REGEN measurement after closing the panel is the next baseline; these numbers alone do not establish that ordinary regeneration is slow or that replacing it with a screen repaint preserves the effect.

The user subsequently confirmed that ordinary REGEN finishes quickly after closing the panel. This narrows the delay to regeneration with this overrule active. The subsequent controlled change replaced SetIdFilter with SetCustomFilter and an IsApplicable implementation backed by a HashSet of the same inventory IDs. Base drawing flags, colors, targets, and regeneration remain unchanged. Registration output includes `(HashSet filter)` to identify the experimental build. This tests the built-in filtering path; its internal complexity and responsibility for the delay are not established. The repeat measurement is recorded below; the broader native visual correctness checks remain open.

### User-confirmed improvement

With the HashSet applicability filter, the user reported that the change helped. The same inventory size (4,389 identifiers), now with 279 targets, took 3.7 ms to collect, 2.3 ms to register, and 527.8 ms to regenerate. The 4,396 SetAttributes calls accumulated 1.3 ms in the base implementation and 2.0 ms in the color override. Reported regeneration time improved by approximately 40 times. Because the target selection differs, this is not an identical-workload benchmark, and it does not establish the internal implementation or complexity of SetIdFilter.

The HashSet filter is retained. Diagnostic instrumentation and its option have been removed. This user check confirms the practical performance improvement on the tested drawing; it does not close the separate block/text, viewport-isolation, DBMOD, and lifecycle verification gates.
## September 20 verification attempt

At commit `7512653`, a fresh `dotnet build CadLens.slnx -c Debug --nologo` completed with zero warnings and errors. `dotnet test CadLens.slnx -c Debug --no-build --nologo` passed all 28 tests (Core 7, Lenses 5, host 11, UI 5), with none skipped. Rider inspections were not rerun.

Civil 3D 2026 was launched and a new blank metric drawing was opened. The automation could not visibly enter commands afterward, including after a session reset and renewed window selection. Loading the rebuilt plugin was not confirmed; no graphics scenario was executed or marked passed. The native checks in tasks 2.3-2.5 remain open. Source inspection also confirms that the current overrule has no viewport-specific rendering filter.

## Explorer navigation and filters

The panel renders generic child lists and detail fields using the existing NavigationState. Back, ancestor/root breadcrumbs, Previous/Next and a one-based counter are connected. Generic filter descriptors provide the snowflake/lightbulb controls and tooltips; enabled identities flow through ExplorerActions to the lens provider. Both options start disabled in a new view model. Refresh preserves valid ancestors, excluded selections return to the root, and failed filter reads clear stale navigation. Context changes and disposal reject late successful reads.

Tasks 5.1 and 5.3 are complete for the managed UI. Task 5.2 remains open for actual Focus availability. Focus is visibly disabled with an explanation; navigation does not invoke host graphics or move the view. The separate Highlight selection action retains its preselection behavior. Tasks 2.3-2.5, 4.2 and native lifecycle checks remain open for user-run AutoCAD verification.

Debug build: zero warnings and errors. All 36 tests pass (Core 7, Lenses 5, host 11, UI 13). Added coverage exercises navigation, breadcrumbs, endpoints/singletons, filter independence and fresh-session defaults, refresh reconciliation, empty results, pending context changes and failed filter reads. Standalone WPF renders cover root and detail states at 370 x 660 and 300 x 450 DIPs; long details scroll and the header/close control stays outside scrolling content. This does not establish host behavior or multi-monitor DPI support.

Manual handoff: load the rebuilt output, run CADLENS and Refresh; open a layer, a type and an object; use Previous/Next, Back and breadcrumbs; toggle frozen/off inclusion separately and together. Check that excluded selected layers return to the root, hidden objects remain hidden, and browsing leaves the drawing view unchanged. Focus remains unavailable in this slice.

JetBrains InspectCode 2026.2.2 completed after the final UI changes with no warning/error issues. The previous unused-property and XAML resource warnings are resolved by consuming the descriptors and using a local button template; the lifetime test no longer combines using with explicit disposal. `openspec validate first-layers-lens --strict` passes. Release was not rerun for this slice.
## Remaining-task verification, September 20

Added five tests against the actual ExplorerComposition and ExplorerActions sources, with native snapshot/highlight operations replaced at their interfaces. They verify missing-registration rejection, singleton-to-scoped rejection at build time, rejection of scoped resolution from the root, fresh scoped state and reset filters after reopening, exactly-once disposal of the fixture source, and absence of DI/AutoCAD references in Core/Lenses/UI. They do not certify native adapter construction, window hosting, or ExplorerOwner event cleanup; task 1.5 remains open for the complete native graph.

Debug and Release solution builds both completed with zero warnings/errors. All 41 tests passed in each configuration (Core 7, Lenses 5, host queue/extensions 11, UI/composition 18), with none skipped. JetBrains InspectCode completed with zero warning/error results (29 informational notes). OpenSpec strict validation passed. This completes task 7.1 for the current implementation.

Full explorer integration remains blocked by tasks 2.3-2.5, as required by design decision 6. Source inspection confirms the current SetAttributes overrule has no per-viewport filter. Native text/block/color rendering, adjacent viewport isolation, cleanup/property comparisons, and overrule coexistence were not executed in this session. No native computer-control surface is available in the current tool session. Focus, navigation-driven emphasis, automatic context/edit refresh, and their host verification remain unfinished. No spec gate was relaxed and no main specification was changed or archived.

## Explicit Focus implementation, September 20

Focus now uses the current node's generic action descriptor and immutable object identifiers. Browsing, Previous/Next, Back, and breadcrumbs never invoke Focus. The action runs through the existing host queue, captures document/space/viewport before queuing, rechecks context inside the callback, reads current entity bounds in a read-only transaction, and skips erased, foreign-database, wrong-space, and unusable-bounds targets. Resetting the UI context cancels pending work as well as rejecting late results.

The adapter transforms extents into active-view coordinates, preserves aspect ratio, and adds 10% padding. Point-sized targets are centered at the existing view scale. It does not change visibility, the current layer, UCS, or viewport locking. Locked layout viewports, perspective/clipped views, invalid view sizes, and sets without usable bounds return an explanation without setting a new view. The transform follows the local PikTools EditorExtensions.WorldToEye pattern and [Autodesk's current-view example](https://help.autodesk.com/cloudhelp/2027/DEU/OARX-DevGuide-Managed/files/GUID-FAC1A5EB-2D9E-497B-8FD9-E11D2FF87B93.htm).

Added managed coverage for wide/tall/line/point targets, invalid and overflowing dimensions, explicit group/object targets, unavailable results, provider-disabled actions, and cancellation/late status after a context change. These tests do not execute Autodesk's matrix, extents, viewport, or Editor APIs. Native fitting in ordinary/twisted/oblique views, partially erased groups, hidden geometry, and locked viewports remains to be checked. Tasks 4.3 and 5.2 therefore remain open. Navigation emphasis still returns unavailable through IHostActions until the native graphics gate passes; the separate selection-highlight preview is unchanged.

Manual check: rebuild/reload the plugin, run CADLENS, Refresh, open a layer/type/object, and click Focus. Verify that only Focus moves the view; repeat with a twisted view, a point, a partly erased group, and included hidden objects. In a locked layout viewport, expect an unavailable explanation and unchanged viewport locking/view. Switch drawing/space or close the panel while a Focus request waits for an active command; it must not execute against the new context. Do not treat these instructions as passed host evidence.

Verification: Debug and Release solution builds each completed with zero warnings/errors; all 58 tests passed in each configuration (Core 7, Lenses 5, host queue/extensions/view fitting 24, UI/composition 22), none skipped. Final JetBrains InspectCode reported zero warning/error results and 29 informational notes. Strict OpenSpec validation and git diff --check passed. The standalone detail render at 300 x 450 DIPs was visually inspected; the header and close control remain reachable with scrollable details. No native AutoCAD scenario was executed.

## Focus simplification after user report

The user reported that clicking Focus caused no visible view change. The previous managed tests did not establish native success. Simplified the path to collect world-space extents, commit/dispose the read transaction, then call Common.AutoCAD.EditorExtensions.Zoom, adapted from the supplied PikTools implementation. Removed the separate ViewFit record and its 13 pure sizing cases; the extension retains aspect preservation, padding, and point-target scale handling. Removed the blanket clipping rejection; perspective and locked layout viewport guards remain.

The previous SetCurrentView ran inside a transaction that was disposed without Commit. Moving it after the transaction removes that potential rollback interaction; this is a candidate correction, not a host-confirmed diagnosis. Debug builds with zero warnings/errors and all 45 remaining tests pass. Native retest is still required; no graphics gate or Focus task is marked complete.

## Navigation highlighting trial

After confirming that the simplified Focus now works in the tested case, the user explicitly requested trying automatic highlighting from the panel. The exact host/version and full Focus fixture matrix were not supplied; this confirms that case only.

Layer/type/object navigation and Previous/Next now send their immutable target sets through IHostActions to the existing EntityHighlightActions queue. Back and breadcrumbs restore ancestor targets; All sends an empty set and clears the effect. Refresh and filter reads clear old effects first, then reapply only the reconciled current selection. Failed reads therefore cannot leave old highlights. Pending navigation disables conflicting commands, and context changes/closing cancel the request and reject late UI status. None of these paths calls Focus.

Both implied selection and panel targets share the same host action implementation. It rechecks document, space, and viewport before executing, intersects targets with the current direct-space inventory, rejects erased IDs, and clears the old effect if no requested targets survive. The HashSet graphics filter and rendering strategy are unchanged.

This is the user-requested integration trial, not acceptance of tasks 2.3-2.5 or 4.2. Native block/text/color behavior, viewport isolation, drawing-property comparisons, and other-overrule coexistence remain unverified. The current overrule still has no per-viewport rendering filter. The experiment does not satisfy the full graphics specification until those checks pass.

Manual trial: reload the rebuilt plugin in a fresh host session, run CADLENS and Refresh, then open a layer, a type, and an object. Check that emphasis narrows while the view stays still; Previous/Next changes the emphasized object, Back expands the targets, and All/close restores normal appearance. Start with a single Model viewport; this is a bounded test, not a reduction of the final required scope.

Validation: Debug and Release builds completed with zero warnings/errors. All 48 tests passed in each configuration (Core 7, Lenses 5, host queue/extensions 11, UI/composition 25), none skipped. Updated navigation tests verify target narrowing/restoration, no implicit Focus, refresh/filter cleanup, cancellation on context change and close, and recovery after a highlight failure. Final JetBrains InspectCode reports no warning/error findings; strict OpenSpec validation and git diff --check pass. Native rendering was not exercised in this session.

## Automatic refresh

The panel now requests its first inventory automatically. ExplorerOwner observes only the active document's database append, modify, erase, unappend, and reappend notifications. Relevant entity/layer changes set one pending flag; Idle consumes it only when the editor is quiescent and the view model can run a read. Changes arriving while another operation is pending remain scheduled for a later idle event. Viewport-only changes while the editor is quiescent are ignored to avoid a Focus-driven inventory refresh; command-time viewport changes still request refresh.

Document leaving/destruction detaches that drawing's subscriptions, cancels the UI request, clears selection/effects, and disables drawing-dependent commands. Activation attaches the new drawing and schedules a root refresh while retaining the session filters. CTAB/CVPORT/TILEMODE changes reset the context and schedule a read. Idle also reconciles the active document as a fallback. Closing/termination detach the new event handlers along with existing cleanup. A background drawing closing does not reset the observed active drawing.

Managed tests cover no-drawing command disablement/recovery, retained filters with cleared navigation on drawing switch, and existing refresh reconciliation/cancellation behavior. They do not exercise native database events, their timing, redraw behavior, or shutdown. Tasks 6.2 and 6.3 remain open for host checks. Manual trial: open CADLENS without clicking Refresh; create/erase objects, Undo/Redo, rename a layer, switch drawings/layouts, then close the last drawing and open another. Check counts, nearest-valid selection, cleared old effects, and restored command availability.

Validation: Debug and Release builds completed with zero warnings/errors, and all 50 tests passed in each configuration (Core 7, Lenses 5, host queue/extensions 11, UI/composition 27), none skipped. Final JetBrains InspectCode has no warning/error findings. Strict OpenSpec validation and git diff --check passed. Native automatic refresh has not been exercised in this session.