# Tasks

## 1. Project boundaries and contracts

- [x] 1.1 Add `CadLens.Core`, `CadLens.Lenses`, and `CadLens.UI` to the solution with the references and target frameworks from design decision 1, root namespaces, and folder Namespace Provider settings; verify the project graph keeps AutoCAD out of Core/Lenses/UI and Lenses out of UI, and build with zero warnings.
- [x] 1.2 Add the conditional WPF UI 4.3.0 dependency in `Directory.Build.props`, scoped UI resources, and dependency copying for the plugin; verify restore/build succeeds, required managed dependencies reach the output directory, and AutoCAD host assemblies are not copied.
- [x] 1.3 Define the minimal Core lens/presentation contracts, native object ID wrappers and typed host-action results; verify a fake non-layer lens can supply groups, fields, filters, and actions without AutoCAD or WPF types.
- [x] 1.4 Add Core/Lenses test projects and centrally configured test dependencies; run a real initial contract test and verify test discovery reports executed tests rather than an empty successful run.
- [ ] 1.5 Add `Microsoft.Extensions.DependencyInjection` with an exact compatible version and conditional host reference in `Directory.Build.props`; establish the AutoCAD composition root with scope/build validation and an explorer-scope factory, then verify restore and provider validation, rejection of singleton-to-scoped dependencies, and no container dependency in Core/Lenses/UI. Extend graph validation as concrete implementations are registered.
  - September 20: added five managed composition tests for missing registrations, singleton/scoped validation, root resolution, fresh session scopes/disposal, and assembly boundaries. Native adapter construction and the full host graph remain unverified; this task stays open.

## 2. Early AutoCAD integration checks

- [ ] 2.1 Implement one queued host task service with application-context execution, a document check and lock, cancellation, and native-callback exception containment; verify queue behavior with controllable native callbacks and execute a read-only request from a modeless callback in AutoCAD.
- [ ] 2.2 Create the minimal modeless panel and host window owner using locally scoped WPF resources, resolving the window/session graph from an explicit explorer DI scope on the host UI thread; verify in AutoCAD that it opens, drags, closes, returns keyboard input to the drawing, and loads without resource or dependency errors.
- [ ] 2.3 Implement an initial temporary visualization adapter for known test-object sets, with scoped graphics overrules and idempotent removal; verify selected geometry is emphasized, other geometry is dimmed, and closing restores appearance without entity/layer property changes.
- [ ] 2.4 Exercise text, hatches, explicit/ByLayer/ByBlock colors, repeated block definitions on different insertion layers, hidden entities, and custom/proxy entities where available; record which fixtures pass and fix rendering exceptions within the adapter, using render-only copies only if needed and disposing them deterministically.
- [ ] 2.5 Verify graphics isolation across Model, paper space, activated layout viewports, adjacent viewports, and documents, including coexistence with another overrule; record host/version, property comparisons, and cleanup results. Treat unmet dimming or isolation requirements as a blocker to full explorer integration, not as permission to ship highlighting alone.

## 3. Snapshot collection and Layers implementation

- [ ] 3.1 Define the Layers snapshot-source contract inside `CadLens.Lenses` and implement its AutoCAD adapter with active-space resolution and read-only transactions; verify fixtures distinguish Model, the active sheet, other sheets, and activated viewport model space, and that pan/zoom alone leaves counts unchanged.
- [ ] 3.2 Collect layer status, viewport-specific freezing, assigned layer, primitive type, and document-scoped object keys without retaining live database objects; verify direct block/xref insertions count once, their contents are not counted, and unknown primitive types retain distinct identities.
- [x] 3.3 Implement default-off frozen/off inclusion filters, normal treatment of locked layers, and omission of empty layers; verify all combinations of global freeze, viewport freeze, off, and locked states with focused unit tests, including layers requiring both toggles.
- [x] 3.4 Implement layer/type grouping, deterministic ordering, hidden-status details, and layer-oriented previews; verify the 180 polylines + 48 lines + 20 arcs fixture totals 248, each object belongs to one type group, and object order remains stable for an unchanged snapshot.

## 4. Exploration and host actions

- [x] 4.1 Implement generic navigation state for groups, types, and individual objects, with breadcrumbs, Back, Previous/Next, and a one-based counter; verify ancestor restoration, root effect clearing, singleton/end-point controls, and no implicit Focus calls during navigation.
- [ ] 4.2 Connect generic emphasis requests to the verified host visualization adapter; verify layer-to-type-to-object navigation narrows emphasis, Back expands it correctly, and excluding the selected layer clears its effects and returns to the list.
- [ ] 4.3 Implement lazy live-object bounds resolution and explicit Focus using active-view coordinates, aspect ratio, and named padding; verify groups and objects fit in ordinary and twisted views, invalid/erased targets are reported, and locked viewports are not unlocked.
- [ ] 4.4 Preserve actual visibility independently of inventory inclusion; verify off/frozen objects remain navigable with explanatory details, Focus does not reveal them, and exploring a layer never changes AutoCAD's current layer.

## 5. Compact UI

- [x] 5.1 Build the single floating panel with a draggable header, close control, lens/space labels, virtualized content, and generic detail rendering; verify with fake-provider fixtures that details replace the list and no view references concrete Layers or AutoCAD types.
- [ ] 5.2 Bind breadcrumbs, Back, counts, object details, Previous/Next, counter, and explicit Focus availability; verify the full navigation path against the `lens-explorer` scenarios and confirm selection does not move the view.
  - Navigation bindings and unit tests are complete. Focus stays disabled pending task 4.3; native emphasis remains gated on tasks 2.3-2.5 and 4.2. AutoCAD checks are performed by the user.
- [x] 5.3 Render generic boolean filter descriptors as the snowflake/lightbulb controls for Layers, with labels/tooltips and clear inactive states; verify both start off in a new session, each updates results independently, and locked layers have no exclusion control.

- [ ] 5.4 Apply the compact tactical layout with softer corners, readable accent colors, keyboard focus indication, and restrained transitions; inspect rendered states with long names, empty results, many groups, hidden objects, and unavailable actions, and verify scrolling keeps the header and close control reachable.
- [ ] 5.5 Verify the hosted panel at multiple DPI settings and constrained working areas, including dragging between monitors where available; confirm full-name tooltips, breadcrumb navigation, keyboard use, and normal AutoCAD input remain usable, recording any unavailable test environment explicitly.

## 6. Session lifecycle and command integration

- [ ] 6.1 Wire the AutoCAD-created `CADLENS` entry point through the composition owner to resolve the constructor-injected Layers/session/UI graph from an explorer scope while retaining its English greeting; verify the complete graph validates, first invocation returns control without command-line input, repeated invocation reuses one panel and scope, and reopening creates fresh scoped instances with both toggles reset.
- [ ] 6.2 Handle document activation/destruction and space/viewport transitions with UI reset and refresh while retaining the explorer DI scope and replacing document-bound state; verify switches release old subscriptions/effects, scoped adapters do not retain the opening document, stale requests cannot publish or act on another document, and closing the last drawing disables drawing-dependent actions.
- [ ] 6.3 Coalesce database changes at safe command boundaries and revalidate live action targets; verify edit, erase, undo/redo, layer rename/status changes, and a queued action against a deleted object refresh results or restore the nearest valid ancestor without stale highlights.
- [ ] 6.4 Route close, initialization failure, and termination through idempotent cleanup: stop requests, coordinate pending callbacks, perform host-sensitive cleanup in the correct context, then dispose the explorer scope and finally the root provider on termination; verify normal and failed opens dispose container-owned resources exactly once, AutoCAD-owned objects are not disposed, no scoped service is used after disposal, other overrules remain active, and explicit Focus navigation is not undone on close.

## 7. Integrated verification and handoff

- [x] 7.1 Run the complete solution tests and build in Debug and Release with zero compiler/analyzer warnings, then check Rider inspections separately; record executed-test counts and fix findings, distinguishing any unavailable inspection tooling from a passing check.
- [ ] 7.2 Run the command, inventory/filter, navigation, Focus, and lifecycle scenarios from all three delta specs in supported AutoCAD hosts; record exact host/version and scenario outcomes, and label Civil 3D evidence separately rather than treating it as standard AutoCAD 2025/2026 coverage.
- [ ] 7.3 On a representative large drawing, measure snapshot time and redraw/navigation responsiveness, then repeatedly switch selections and open/close the explorer; record object counts, timings, and resource behavior, and resolve observed freezes, unbounded growth, or stale effects.
- [ ] 7.4 Compare geometry/layer properties and `DBMOD` before/after read-only exploration and cleanup; verify Focus separately as an intentional view operation, and perform a clean-load smoke check using the packaged dependency output.
- [ ] 7.5 Update user documentation with launch/close behavior, scope, inclusion controls, navigation, and actual verification limits; verify every documented action against the finished UI, review the implementation against the specs, and run `openspec validate first-layers-lens --strict` without archiving or changing the main specs yet.