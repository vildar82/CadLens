# Verification — lens-panel-and-switching

Initial verification: 2026-09-20. Simplification follow-up: 2026-09-26.

## Implemented behavior

- New sessions start inactive in a 300 × 52 bar. Layers activates the existing explorer; repeated CADLENS invocation still brings the same window forward.
- Collapse retains navigation and inclusion settings, invalidates late results, cancels pending drawing work, waits for it to settle, and clears effects through the existing host queue with an independent lifetime token.
- Pending cleanup disables reactivation and appears as an ellipsis with a status tooltip. Failed/unavailable cleanup remains explained in the compact status tooltip; Close stays available.
- Reactivation reads fresh data and restores valid targets without Focus. Root restoration remains unselected; erased targets fall back through the existing navigation reconciliation.
- Drawing edits do not trigger database subscriptions or inventory reads. Activation, Refresh, filter changes, and active context changes load data. Closing lets modules cancel work and clear their effects before the host queue stops.
- Expanded dimensions are remembered within the session. Expansion clamps to the current monitor work area, using the window's device transform. No transition animation was added.

## Executed checks

- Full managed suite: 77 passing tests (Core 4, Lenses 8, AutoCAD stubs 11, UI 54).
- Debug and Release solution builds: zero warnings and errors.
- Roslynator analysis of CadLens.UI at warning severity: zero diagnostics. This is not a Rider inspection result.
- Real XAML rendered at 370 × 660 and 300 × 450, with inventory and details; compact inactive, disabled, pending, and failed states rendered at 300 × 52. Inspected compact inactive/pending and expanded narrow inventory/details images.
- STA window tests verify compact dimensions, resize modes, expanded size restoration, stable top-left position when space permits, and expansion clamping against the available primary work area. These tests do not exercise live monitor changes.
- Controlled managed completions cover collapse during activation/read/emphasis, independent cleanup cancellation, unavailable/throwing cleanup, closing during cleanup, late-result rejection, valid selection restoration, erased-target fallback, retained filters, root restoration, and compact document changes.
- Existing host queue tests cover native-callback cancellation and shutdown ordering using AutoCAD stubs. Existing scoped-composition tests confirm fresh inactive sessions and default filters after reopening.
- Final diff reviewed for lifetime ordering, stale result publication, host boundaries, and unnecessary abstractions. The shared shell depends only on module metadata, views, and lifecycle methods. Layers owns its navigation, filters, commands, and host actions; the existing host queue and graphics implementation are reused.
- `openspec validate lens-panel-and-switching --strict` and `git diff --check` passed.

## Independent lens modules

- ILens exposes a descriptor, its own WPF view, and lifecycle callbacks. ExplorerViewModel consumes all ILens registrations; generated toolbar buttons and a ContentControl discover and display each module automatically.
- LayersView, LayersViewModel, and ILayersActions live in CadLens.UI. Layers data contracts and navigation belong to CadLens.Lenses. The shared shell depends on ILens; lens implementations use folders within the existing UI project.
- Production registers only Layers. A test-only Counter module has unrelated XAML, its own view model and service, and an Increment command. DI composition and an STA window test verify discovery, activation through the generated button, and invocation of its own command. The rendered Counter view was visually inspected.
- Module lifecycle tests cover independent state, activation cancellation, cleanup ordering, failed/thrown cleanup retry, context notifications, and close/context changes during switching.
- Layers tests cover context reloads after pending work, in addition to existing navigation, filtering, cancellation, and focus behavior.
- The full 77-test suite and Debug/Release builds pass after the module refactor. Strict OpenSpec validation and diff checks pass. Roslynator reports no warning diagnostics in the shell or Layers UI.

## Outstanding verification

- No running acad.exe was available. No new build was loaded in AutoCAD or Civil 3D. Native tasks 4.2 and 4.3 remain unchecked, including camera/DWG preservation, cleanup during an active command, close/document/space transitions, and repeated command invocation.
- ExplorerOwner context forwarding and close ordering were source-reviewed, not exercised against real AutoCAD events. Tasks 2.1 and 2.3 remain unchecked pending integration verification.
- Live hover, keyboard focus/toggle/Close, dragging, and multi-monitor/mixed-DPI behavior remain unverified. Tasks 3.1, 3.3, and 3.4 remain unchecked. Rendered images and STA tests do not substitute for these checks.
- Rider/InspectCode was not available on PATH or in the searched JetBrains/tool locations, and no Rider connector was exposed. Task 4.1 remains unchecked despite passing builds, managed tests, Roslynator analysis, and diff review.
- Existing hatch/block native rendering limitations remain unchanged. No host validation from earlier changes is claimed for this implementation.

The change remains active and has not been archived.

## 2026-09-26 follow-up

- In a live AutoCAD session, the user reported that the Layers list appeared but its commands remained disabled with "Working... Finish any active AutoCAD command to continue." No command had been started. The toggle later recovered; the expanded list still did not respond. This is a reported failure, not a completed host check.
- The host queue now waits on `CMDACTIVE` instead of `Editor.IsQuiescent`, which can block queued panel work when no command is active. A focused managed regression test covers that state. The full managed suite passes (79 tests), and the Debug solution build has zero warnings and errors from an alternate output path because AutoCAD holds the normal plugin output DLL open.
- The updated DLLs still need a fresh AutoCAD load and the reported interaction retested. Tasks 2.1, 2.3, 3.1, 4.2, and 4.3 remain unchecked.
- A later large-drawing retest showed repeated `ReadInventoryAsync` calls. The debugger stack contained `RunAsync` → `RefreshPendingInventory` → `RunAsync`, confirming that a pending drawing notification was starting another full read before the previous call unwound. The exact AutoCAD object that raised the notification was not captured.
- An intermediate attempt added own-callback event suppression and a delayed refresh. It built with zero warnings/errors and passed 79 managed tests, but had no native confirmation. The user requested a simpler design; that attempt has been removed.

## Simplified execution flow

- Removed both drawing-edit refresh schedulers, delayed retries, duplicate Layers cleanup/busy flags, Layers lifetime/version tracking, and UI completion-source handshakes. The existing native queue remains the only host executor.
- Layers stores its actual operation task and cancellation source. The shell stores its actual activation task and owns switching/cleanup state. Canceled results cannot publish data or status, and cleanup still waits for old work.
- At that stage, drawing notifications did not enqueue a read. Idle edits showed a Refresh hint; notifications during work were ignored. Activation, explicit Refresh, filter changes, and active document/space changes read current data.
- Debug and Release solution builds: zero warnings and errors. Full Debug managed suite: 81 passed (Common 4, Lenses 8, AutoCAD stubs 12, UI 57). Regressions cover edit notifications during/after work, explicit Refresh/reactivation, repeated context changes, and collapse while a context reload is waiting.
- Independent source review found no actionable issues. Strict OpenSpec validation and `git diff --check` passed. InspectCode remains unavailable on PATH; no Rider inspection result is claimed.
- Validation output: `C:/Users/vilda/AppData/Local/Temp/CadLens-validation/simplified-flow/bin/CadLens.AutoCAD/debug/CadLens.AutoCAD.dll`. This build was not loaded into AutoCAD. In a fresh host session, verify that the large drawing finishes loading, rows and filters respond, drawing edits require Refresh, and collapse/close leave no effects. Also verify active document/space switches load the new root without repeated reads.

## Removed drawing-edit notifications

- ExplorerOwner no longer subscribes to database object events. ILens and the shared UI no longer forward drawing-edit notifications. Refresh remains the explicit action for reading edits in the active space; document and space context changes still reset and reload the active lens.
- Debug solution build: zero warnings and errors. Ten focused UI tests passed; strict OpenSpec validation passed. The full UI suite had 15 failures in tests expecting Auto Highlight on by default, while the worktree's existing independent-navigation change defaults it off. These failures are outside this notification removal and remain unresolved here.
- Native AutoCAD behavior and Rider inspections were not run for this change.

## Closure — 2026-09-26

The user reported that they checked the current implementation in AutoCAD and that everything works. This is user-reported acceptance, not a new agent-run host check. The host version and a result for each detailed scenario were not supplied, so the remaining native scenario checkboxes stay unchecked. The Rider-only inspection checkbox also remains unchecked; prior managed build and test results are recorded above.

The earlier failure and unverified-host notes are historical records of intermediate builds. The user's acceptance applies to the current implementation. The change was closed with these verification limits retained.
