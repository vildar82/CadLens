# Implementation verification

## Current preview

`CADLENS` opens or activates the single modeless panel. `CADLENSVERIFY` has been removed at the user's request. Refresh reads the active-space inventory; Highlight selection captures the drawing's preselection before queuing work, then revalidates the objects before applying the rendering candidate. Clear highlight removes the owned effect without moving the camera.

The panel now uses CommunityToolkit.Mvvm 8.4.0, locally scoped WPF UI resources, a compact inventory, explicit busy/error feedback, and a clearer visual hierarchy. This is still a preview: inclusion toggles, drill-down, breadcrumbs, object browsing, and Focus are not connected to the panel yet. The complete first-layers-lens change remains open.

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