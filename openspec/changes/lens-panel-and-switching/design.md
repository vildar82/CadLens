# Design

## Context

See proposal.md for motivation and the two delta specifications for observable behavior. The original ExplorerViewModel owned both navigation and the panel lifecycle; these responsibilities are now split between LayersViewModel and the shared shell. NavigationState.Reset reconciles saved node identities with refreshed data. ExplorerOwner forwards document and space changes. ReadInventoryAsync clears effects, reads, restores navigation, and reapplies selection emphasis.

These existing paths need activation awareness; hiding the content alone would allow background refresh to reapply effects while compact.

## Goals / Non-Goals

Goals: extend the current window and session, keep activation state explicit, reuse navigation reconciliation and the native work queue, and make compact mode genuinely small.

Non-goals: runtime assembly discovery, additional production lens implementations, a new session framework, another host execution service, persistent preferences, or graphics-adapter redesign.

## Decisions

### 1. One window with a persistent lens bar

Keep ExplorerWindow as shared chrome. Place a compact bar above a ContentControl hosting the selected module view. Move the existing exploration content and its resources into LayersView. The bar contains a drag area, a scrollable list of registered lens toggles, and Close. Labels come from module descriptors, without a drawing read. Use a styled ToggleButton with a visible keyboard focus indicator and an accessible name. Keep the toggle and Close interactive within the window chrome.

Compact mode contains only the bar. Expanded mode shows the current space, filters, navigation, list/details, and existing actions beneath it. Do not duplicate Layers information in a separate dashboard.

Visual direction: retain the existing dark surface, rounded border, and mint accent. Use a muted inactive button, an accent border and subtle tinted background when active, and restrained hover feedback. A short content-opacity transition can soften expansion; do not animate window dimensions or delay cleanup for an animation. Respect disabled client animations. Exact spacing and icon sizing can be tuned during visual verification.

Alternative: a separate toolbar and lens window. Rejected because the user chose one movable window. Standard tabs are also unsuitable because the selected lens must be toggleable off.

### 2. Size the actual window for each mode

Remove the expanded minimum-size constraint while compact. Size the compact window to its bar; disable resizing in that mode. Restore the last expanded dimensions within the session when activating, with the current explorer dimensions as the initial expanded baseline. Retain expanded resizing and list scrolling.

Keep the bar's top-left position stable when possible and expand below it. Clamp the window to the current monitor working area when expansion would put essential controls off-screen. Keep window size and screen-position handling in the view, separate from lens state.

Alternative: collapse content inside the existing tall window. Rejected because it leaves a large empty area over the drawing.

### 3. One activation property, separate from busy state

Add one lens-active state to ExplorerViewModel, initially false. Derive expanded visibility from the selected LensOption.IsActive and each button checked appearance from its own LensOption.IsActive. Busy state remains separate.

Activation sets the lens active and starts a fresh read using existing inclusion settings. A fresh read on each activation ensures edits made while compact are reflected without adding cache invalidation machinery. The Layers module uses NavigationState.Reset with path preservation in the same context; apply emphasis only to the resulting current node. At the root, leave effects clear.

Deactivation immediately marks the lens inactive, invalidates outstanding results, and cancels the current read/action while retaining navigation and filters. It then requests cleanup through the existing host action path. Do not call ResetContext on collapse because that discards the path.

Alternative: dispose and recreate the explorer scope on every toggle. Rejected because it loses navigation and duplicates lifecycle work.

### 4. Order cleanup and prevent late work from restoring effects

The deactivate action must remain available during a read or emphasis request; it cannot use the ordinary !IsBusy command gate. Activation can wait until cancellation and cleanup settle. Keep Close available throughout.

Layers owns one current operation task and its cancellation source. Collapse, context changes, and close cancel that source; token checks after awaits reject late data and status. Busy state is derived from request ownership. The operation releases ownership only if cleanup has not replaced it. The shell stores the actual activation task and owns switching/cleanup state; its context version prevents a pending switch from activating a module after a document change.

Cancel prior work before queuing ClearAsync. Cleanup uses a session-lifetime token, not the canceled operation token. The existing host queue preserves native ordering; cleanup must not be skipped by the normal UI busy gate. Closing and context changes retain the owner's direct cleanup fallback. Do not stop the host queue merely to collapse the panel.

If cleanup cannot run immediately because AutoCAD is busy, keep the lens inactive and prevent reactivation until cleanup settles. Retain a visible compact status indication or tooltip for pending/failed cleanup rather than falsely reporting that effects are cleared. Contain errors and keep Close usable.

Alternative: cancellation alone. Rejected because it does not remove an effect already applied by a native callback.

### 5. Make drawing-edit refresh explicit

ExplorerOwner does not subscribe to database object changes. Drawing edits do not schedule work; the user can press Refresh to load the active space. This avoids event traffic from graphics regeneration and inventory reads.

Reads happen on activation, Refresh, filter changes, and document/space changes while active. Context changes cancel old work, clear effects and navigation, wait for the old operation, and load the new root. Multiple context notifications waiting on the same operation produce one new read. Compact lenses remain inactive. Preserve inclusion settings across collapse; a new session retains the default-off filters.

Automatic rereads after every database event were removed after the user reported an endless refresh loop on larger drawings. Explicit Refresh is the intentional behavior change in this simplification.

### 6. Keep command and provider composition small

Keep LayersLensProvider as the sole production lens, but make composition support any number of provider/action registrations. CADLENS still opens or activates the same window; the view model's inactive default supplies compact startup. Repeated command invocation only brings the current window forward and does not toggle its mode.

ILens is a WPF module contract in CadLens.UI. It exposes only a descriptor, its own FrameworkElement view, activation/deactivation, context notifications, and synchronous close. No common presentation DTO, filter model, command set, or view-model base class is required. Modules and their constructor dependencies are registered in DI; ExplorerViewModel consumes IEnumerable<ILens> and ExplorerWindow uses a ContentControl to host ActiveView.

The shared shell tracks toolbar selection and lifecycle ordering only. It cancels the outgoing activation lifetime and waits for activation to settle before deactivation, which must settle any additional module-owned work. Successful cleanup is required before another module can activate. Failed cleanup is reported and retried on the next activation attempt. Close notifies every module before the host queue and DI scope are disposed, with a host-termination flag to prevent unsafe redraw during shutdown.

LayersLens composes LayersView and LayersViewModel in the CadLens.UI/Lenses/Layers folder. No project per lens is required. Its provider and presentation/navigation models live in CadLens.Lenses; its AutoCAD service implements ILayersActions. Those contracts are private to the Layers architecture and impose no requirements on other modules. Each module owns its state and context invalidation. Views are created lazily on the WPF thread; enumeration alone performs no drawing reads.

The bar binds an ItemsControl to immutable module options in registration order, with horizontal scrolling when needed. Reject empty registrations and duplicate identities before drawing access. No runtime assembly discovery, additional host queue, or new NuGet dependency is needed.

## Risks / Trade-offs

- Late reads or emphasis after collapse: cancel the owned operation, check its token after awaits, order cleanup through the existing queue, and test controlled late completions.
- Native cleanup can wait behind an AutoCAD command: show pending state, block reactivation until settled, and verify in the host without claiming synchronous native completion.
- Saved paths can reference erased objects: refresh on activation and reconcile identities to the nearest valid ancestor or root.
- Compact dimensions and expansion can behave differently under DPI scaling: check both modes near screen edges and at available DPI settings; keep controls reachable.
- Existing hatch/block rendering issues remain: reuse the adapter and record them as accepted limits; this change must still remove its owned effects correctly.

## Verification

Use focused view-model tests for initial inactivity, collapse during work, late results, unchanged-path restoration, deleted targets, filters, context changes, and repeated invocation. Reuse existing navigation tests rather than duplicating them. Build with zero warnings and check Rider inspections separately after implementation.

In AutoCAD, verify startup, toggle/collapse after highlighting, cleanup while commands are busy, close in both modes, edits while compact, document/space transitions, keyboard access, window positioning, and camera preservation. Managed tests do not establish native cleanup or window behavior.

## Migration Plan

No persisted state or drawing schema changes. Update the command and explorer documentation with the new startup behavior. Deploy the normal plugin output and perform a fresh-load check. Rollback is reverting this feature and rebuilding; it requires no drawing migration.
