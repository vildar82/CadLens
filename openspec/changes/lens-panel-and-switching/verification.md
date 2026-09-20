# Verification — lens-panel-and-switching

Date: 2026-09-20.

## Implemented behavior

- New sessions start inactive in a 300 × 52 bar. Layers activates the existing explorer; repeated CADLENS invocation still brings the same window forward.
- Collapse retains navigation and inclusion settings, invalidates late results, cancels pending drawing work, waits for it to settle, and clears effects through the existing host queue with an independent lifetime token.
- Pending cleanup disables reactivation and appears as an ellipsis with a status tooltip. Failed/unavailable cleanup remains explained in the compact status tooltip; Close stays available.
- Reactivation reads fresh data and restores valid targets without Focus. Root restoration remains unselected; erased targets fall back through the existing navigation reconciliation.
- Automatic refresh requires active drawing commands. Activation consumes compact-mode refresh demand; document observation and direct context cleanup remain in place. Closing disposes the view model before stopping and draining the host queue.
- Expanded dimensions are remembered within the session. Expansion clamps to the current monitor work area, using the window's device transform. No transition animation was added.

## Executed checks

- Full managed suite: 65 passing tests (Core 7, Lenses 5, AutoCAD stubs 11, UI 42).
- Debug and Release solution builds: zero warnings and errors.
- Roslynator analysis of CadLens.UI at warning severity: zero diagnostics. This is not a Rider inspection result.
- Real XAML rendered at 370 × 660 and 300 × 450, with inventory and details; compact inactive, disabled, pending, and failed states rendered at 300 × 52. Inspected compact inactive/pending and expanded narrow inventory/details images.
- STA window tests verify compact dimensions, resize modes, expanded size restoration, stable top-left position when space permits, and expansion clamping against the available primary work area. These tests do not exercise live monitor changes.
- Controlled managed completions cover collapse during activation/read/emphasis, independent cleanup cancellation, unavailable/throwing cleanup, closing during cleanup, late-result rejection, valid selection restoration, erased-target fallback, retained filters, root restoration, and compact document changes.
- Existing host queue tests cover native-callback cancellation and shutdown ordering using AutoCAD stubs. Existing scoped-composition tests confirm fresh inactive sessions and default filters after reopening.
- Final diff reviewed for lifetime ordering, stale result publication, host boundaries, and unnecessary abstractions. No new service, dependency, lens registry, or shared graphics behavior was introduced.
- `openspec validate lens-panel-and-switching --strict` and `git diff --check` passed.

## Outstanding verification

- No running acad.exe was available. No new build was loaded in AutoCAD or Civil 3D. Native tasks 4.2 and 4.3 remain unchecked, including camera/DWG preservation, cleanup during an active command, close/document/space transitions, and repeated command invocation.
- ExplorerOwner event coalescing and close ordering were source-reviewed, not exercised against real AutoCAD events. Tasks 2.1 and 2.3 remain unchecked pending integration verification.
- Live hover, keyboard focus/toggle/Close, dragging, and multi-monitor/mixed-DPI behavior remain unverified. Tasks 3.1, 3.3, and 3.4 remain unchecked. Rendered images and STA tests do not substitute for these checks.
- Rider/InspectCode was not available on PATH or in the searched JetBrains/tool locations, and no Rider connector was exposed. Task 4.1 remains unchecked despite passing builds, managed tests, Roslynator analysis, and diff review.
- Existing hatch/block native rendering limitations remain unchanged. No host validation from earlier changes is claimed for this implementation.

The change remains active and has not been archived.