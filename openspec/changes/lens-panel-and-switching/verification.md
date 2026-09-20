# Verification — lens-panel-and-switching

Date: 2026-09-20.

## Implemented behavior

- New sessions start inactive in a 300 × 52 bar. Layers activates the existing explorer; repeated CADLENS invocation still brings the same window forward.
- Collapse retains navigation and inclusion settings, invalidates late results, cancels pending drawing work, waits for it to settle, and clears effects through the existing host queue with an independent lifetime token.
- Pending cleanup disables reactivation and appears as an ellipsis with a status tooltip. Failed/unavailable cleanup remains explained in the compact status tooltip; Close stays available.
- Reactivation reads fresh data and restores valid targets without Focus. Root restoration remains unselected; erased targets fall back through the existing navigation reconciliation.
- The shell forwards drawing edits to the active module. Layers coalesces edits during pending work and refreshes only while active. Context changes reach every module. Closing lets modules cancel work and clear their effects before the host queue stops.
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
- Module lifecycle tests cover independent state, activation cancellation, cleanup ordering, failed/thrown cleanup retry, context and drawing notifications, and close/context changes during switching.
- Layers tests cover refresh coalescing during pending work and suppression after collapse, in addition to existing navigation, filtering, cancellation, and focus behavior.
- The full 77-test suite and Debug/Release builds pass after the module refactor. Strict OpenSpec validation and diff checks pass. Roslynator reports no warning diagnostics in the shell or Layers UI.

## Outstanding verification

- No running acad.exe was available. No new build was loaded in AutoCAD or Civil 3D. Native tasks 4.2 and 4.3 remain unchecked, including camera/DWG preservation, cleanup during an active command, close/document/space transitions, and repeated command invocation.
- ExplorerOwner event coalescing and close ordering were source-reviewed, not exercised against real AutoCAD events. Tasks 2.1 and 2.3 remain unchecked pending integration verification.
- Live hover, keyboard focus/toggle/Close, dragging, and multi-monitor/mixed-DPI behavior remain unverified. Tasks 3.1, 3.3, and 3.4 remain unchecked. Rendered images and STA tests do not substitute for these checks.
- Rider/InspectCode was not available on PATH or in the searched JetBrains/tool locations, and no Rider connector was exposed. Task 4.1 remains unchecked despite passing builds, managed tests, Roslynator analysis, and diff review.
- Existing hatch/block native rendering limitations remain unchanged. No host validation from earlier changes is claimed for this implementation.

The change remains active and has not been archived.