# Tasks

## 1. Separate drawing actions

- [x] 1.1 Add Select to the existing visualization and Layers contracts, update implementations and test fakes, and remove selection from Focus. Reuse the shared selection extension with a returned count and empty-input clearing. Verify compilation and confirm that selection has no dependency on bounds, viewport locks, or camera movement.
- [x] 1.2 Keep highlight-only cleanup separate from complete Layers cleanup. Make complete cleanup attempt both selection and graphics clearing and report failure accurately. Verify that disabling Auto Highlight preserves CAD selection and that a failed cleanup is not reported as successful.
- [x] 1.3 Extend immediate cleanup to clear active-editor selection at close and context boundaries, with explicit shutdown handling. Verify existing document-leaving hooks, cancellation guards, and shutdown paths by source review; record native behavior separately.

## 2. Modes and navigation

- [x] 2.1 Add independent Select, Auto Select, and awaited Auto Focus commands to LayersViewModel. Set defaults to Focus off, Select off, Highlight on. Verify root-level toggle availability, disabled manual actions without targets, immediate application when enabling a mode, and independent manual actions using focused managed checks.
- [x] 2.2 Apply enabled modes through the existing navigation method, with Focus last. Verify layer/type/object entry, Previous/Next, Back, and breadcrumbs; selection-only browsing must leave the camera unchanged. Check that unavailable Focus does not prevent selection/highlighting and that root navigation clears effects without changing modes.
- [x] 2.3 Replace Clear with Reset. Verify that Reset clears selection/highlighting, turns off all Auto modes, preserves camera/navigation/filters, stays disabled while busy, and leaves effects cleared during subsequent navigation.
- [x] 2.4 Preserve modes through collapse and restore valid navigation after fresh reads. Verify selection/highlight restoration without Focus, deleted/excluded-target fallback, and late completion after collapse, close, or context changes. Keep the existing cleanup failure/retry behavior and new-session defaults.

## 3. Integrate layout B

- [x] 3.1 Integrate B's action strip, Reset row, compact centered Back arrow, and breadcrumb/detail spacing into the Layers view with only its required styles. Verify root, group, type, object, empty, error, and busy states at 300 by 450 and 370 by 660, including scrolling and visible keyboard focus.
- [x] 3.2 Update Preview adapters and comparison checks to use the production mode commands; remove duplicate simulated mode orchestration. Verify the ordinary preview and both comparison variants still run, with switching/closing canceling pending work and comparison controls remaining outside production UI.

## 4. Integrated verification and handoff

- [x] 4.1 Run the affected managed regression checks and the existing solution tests. Build CadLens.slnx with zero warnings, check Rider inspections separately, and review the diff for regressions and unnecessary code. Record actual results and any unavailable tooling or locked-output limitation.
- [x] 4.2 Update user documentation and add a concise verification record with build/output details, Preview evidence, and the manual AutoCAD scenarios below. Run `openspec validate independent-navigation-modes --strict` and `git diff --check`; keep unexecuted native checks unchecked and the separate panel/switching change intact.

## 5. Native verification by the user

- [ ] 5.1 In a fresh AutoCAD load, verify selection-only browsing at every level, independent manual actions, immediate Auto toggles, Reset/resumption, and disabled Focus with usable Select in a locked viewport. Check deleted targets, hidden layers, model/paper space, and multiple viewports; record host/version and confirm stored geometry, properties, and layer visibility remain unchanged.
- [ ] 5.2 In AutoCAD, verify collapse/reopen without camera movement, close during pending work, document/space changes, and last-drawing closure. Confirm selection and graphics clear without stale effects returning; record results separately from managed checks.
