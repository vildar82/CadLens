# Tasks

## 1. Activation and restoration

- [x] 1.1 Add initially inactive lens state and activation/deactivation commands to the existing explorer view model; verify new sessions perform no inventory read or emphasis until activation, and activation opens the root without invoking Focus.
- [x] 1.2 Preserve navigation and inclusion settings on collapse, then refresh and reconcile the path on activation; verify unchanged selection restoration, root restoration without emphasis, deleted-target fallback, and retained filters with focused managed tests.
- [x] 1.3 Gate drawing actions and result publication on lens activity and operation version; verify deactivation remains available during pending work and controlled late read/emphasis completions cannot reactivate the lens, publish stale content, or restore effects.
- [x] 1.4 Order cancellation and cleanup through the existing host actions, using a cleanup token independent of the canceled operation; verify cleanup is not skipped while busy, old completions cannot reset newer request ownership, and reactivation waits for cleanup to settle. Cover unavailable/failed cleanup without claiming success and keep Close usable.

## 2. Host session integration

- [ ] 2.1 Make automatic reads require an active lens while retaining document observation; verify edits while compact trigger no background read or emphasis, activation reads fresh data without an immediate duplicate refresh, and active-mode refresh still works.
- [x] 2.2 Preserve compact/inactive state across document and space changes while discarding old navigation; verify the next activation uses the new context root, closing the last drawing disables drawing actions, and existing context cleanup remains effective.
- [ ] 2.3 Keep CADLENS startup compact and repeated invocation state-preserving; verify one window/session is reused in either mode and closing/reopening starts a fresh inactive session with default filters. Verify closing during pending activation or cleanup cannot recreate effects.

## 3. Window and interaction

- [ ] 3.1 Add the persistent lens bar with a styled Layers toggle, drag area, and Close; keep existing exploration content below it and expose pending/failed cleanup in compact mode. Visually verify inactive, active, hover, keyboard-focus, disabled, and pending states with no future-lens placeholders.
- [x] 3.2 Make compact mode size the actual window to the bar and disable resizing; restore expanded dimensions within the session and preserve scrolling. Verify no empty expanded area remains when compact and all essential controls remain reachable when expanded.
- [ ] 3.3 Keep the bar position stable where possible and constrain expansion to the monitor working area; verify expansion near screen edges and dragging at available DPI settings, recording unavailable monitor/DPI cases explicitly.
- [ ] 3.4 Apply the dark surface, mint active accent, and restrained interaction feedback using existing resources; verify keyboard toggle/close access and that any content transition respects disabled animations and never delays cleanup.

## 4. Integrated verification and documentation

- [ ] 4.1 Run the full managed test suite and Debug/Release solution builds with zero warnings; run focused Rider inspections separately and review the final diff for regressions and unnecessary abstractions. Record actual results and any tooling limitations.
- [ ] 4.2 In AutoCAD, verify fresh-load compact startup, activation, layer/type/object navigation, collapse cleanup, restoration, root behavior, repeated CADLENS invocation, and close/reopen. Verify cameras remain unchanged except for explicit Focus and drawing geometry/properties remain unchanged; record host/version and outcomes.
- [ ] 4.3 In AutoCAD, verify collapse during pending work or an active command, edits/deletion while compact, document/space switches, last-drawing closure, and close during pending work. Confirm no stale effects return and record any failures separately from the accepted hatch/block rendering limitations.
- [x] 4.4 Update user documentation and add a concise verification record describing compact startup, toggle behavior, restoration, and actual checks. Run openspec validate lens-panel-and-switching --strict and keep unexecuted checks unchecked; do not archive the change as part of implementation.

## 5. Independent lens modules

- [x] 5.1 Replace universal provider/action contracts with ILens identity, own view, lifecycle, and notifications; register modules through DI and keep production limited to Layers.
- [x] 5.2 Move exploration XAML, view model, provider, presentation/navigation models, and actions into the Layers implementation; keep the shared UI independent of Layers assemblies.
- [x] 5.3 Preserve cancellation, cleanup ordering, failure/retry, close, and context guards when switching arbitrary modules; keep Layers refresh coalescing inside the module.
- [x] 5.4 Verify a Counter fixture with independent XAML, view model, service, and Increment command through DI and the real shell ContentControl. Update the extension guide and record actual verification limits.