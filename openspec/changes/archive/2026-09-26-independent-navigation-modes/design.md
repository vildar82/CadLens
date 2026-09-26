# Design

## Context

See [the proposal](proposal.md) and [behavior specification](specs/lens-explorer/spec.md).

`LayersViewModel` already routes Enter, Back, breadcrumbs, Previous, and Next through `NavigateAsync`. It also owns busy state, cancellation, and navigation restoration. AutoCAD Focus currently calls both `Editor.Zoom` and `Editor.SelectObjects`. Highlight cleanup currently removes only graphics.

Variant B runs in Preview with a temporary wrapper that simulates selection. Production integration must put that behavior into the existing Layers actions and view model.

## Goals / Non-Goals

**Goals:** Keep one navigation path, one host task queue, and explicit methods for the three drawing actions. Reuse the selected WPF layout and shared selection extension.

**Non-goals:** A mode framework, another execution service, persisted settings, new lenses, or changes to inventory and navigation models. The shared explorer shell remains independent of Layers controls.

## Decisions

### 1. Separate native selection from camera focus

Add `SelectAsync` to `IObjectVisualizationService` and `ILayersActions`, following their existing result and cancellation patterns. Remove selection from the native Focus method and correct its status text.

Implement selection through the existing host queue. Capture the active document/space/viewport and verify them inside the queued operation, as Focus already does. Resolve valid direct current-space objects through `Editor.SelectObjects`; keep this filtering in the shared extension. Return the selected count from that extension so a nonempty request with no surviving targets can clear selection and report why. Empty input means clear selection and can skip the object-read transaction.

Selection does not read bounds or inspect viewport lock/perspective restrictions. Those checks stay in Focus. A combined Focus-and-Select operation was rejected because it would keep selection dependent on camera eligibility.

### 2. Keep mode state and commands in LayersViewModel

Use three booleans, initially Focus off, Select off, Highlight on. Add Select and Auto Select commands; give Auto Focus an awaited toggle command like Auto Highlight. Bind toggle state one-way and change it through its command, avoiding asynchronous work in a property setter.

Use the existing command availability and `ExecuteActionAsync` wrapper for manual actions, toggles, navigation, and Reset. Select and Highlight require a nonempty current target; only Focus additionally requires focus eligibility. Manual commands call only their own action.

Do not copy the Preview wrapper's property-change-driven selection into production. Applying selection from `Current` notifications could run outside the existing cancellation and busy-state flow.

### 3. Apply modes through the existing navigation method

After navigation changes the target:

1. At the root, clear selection and highlighting and return without Focus.
2. For a target, apply Auto Select if enabled.
3. Apply Auto Highlight if enabled; otherwise clear only highlighting.
4. Apply Auto Focus last if enabled and available.

Keep these as short, named steps in `LayersViewModel`, with cancellation checks between awaited host calls. Use ordinary conditionals, not a collection of mode handlers. Running Focus last ensures unavailable bounds or a locked view cannot prevent selection and highlighting. Preserve any unavailable result in the final status instead of overwriting it with a later success message; cancellation stops the sequence.

After inventory refresh or reactivation, restore the valid path using the existing navigation model and apply enabled selection/highlighting only. Do not call the navigation Focus step. Refresh already clears effects before reading; use the same cleanup for selection, so excluded or deleted targets cannot remain selected.

### 4. Distinguish highlight cleanup from complete cleanup

Retain a clearly named highlight-only operation for Auto Highlight off and navigation while it is off. Use empty Select targets for selection-only clearing. Make the Layers cleanup operation clear both selection and graphics, returning success only when both have cleared; attempt both even if one reports unavailable.

Replace the visible Clear command with Reset. Reset turns off all Auto modes and runs complete cleanup through the existing busy-state wrapper, preserving navigation and the camera. It needs no suppression flag: effects are applied by explicit commands and navigation, not by property notifications or timers.

Collapse continues to cancel and await pending work before cleanup, using the independent cleanup token. Close and context changes continue to use synchronous cleanup after cancellation. Extend that path to clear implied selection in the active editor as well as detach graphics. Pass host-shutdown intent explicitly through the Layers immediate-cleanup contract so shutdown skips editor access and regeneration.

Reuse the existing document-leaving and document-destroying notifications, which invalidate the context before it leaves. Do not queue cleanup against whichever document happens to be active later or access a closed document. Keep the shell's existing cleanup failure/retry behavior.

Track whether the Layers session owes cleanup. Activation sets this flag; successful deactivation clears it. This preserves external preselection in an inactive compact session while still retrying immediate cleanup after a failed collapse.

### 5. Integrate B directly into the Layers view

Move the selected three-column strip, Reset row, compact vector Back arrow, and relevant breadcrumb/detail spacing into `LayersView.xaml`. Copy only the styles required by B into Layers resources, reusing the existing theme colors. Keep the actions outside the content scroll area and preserve the checked, disabled, hover, pressed, and keyboard-focus states.

Bind directly to `LayersViewModel`. Keep comparison controls and variant A in Preview. Update Preview adapters and checks for the new action contract, and use the production view model for both candidates so simulated selection no longer needs a second implementation.

## Risks / Trade-offs

- **Highlight cleanup accidentally clears CAD selection:** keep separate methods and check manual Highlight and Auto Highlight off against an existing selection.
- **Old work restores effects after cleanup:** reuse cancellation/context checks; verify late completion after collapse, close, and document changes.
- **Native selection differs from the simulation:** verify deleted targets, hidden layers, locked viewports, model/paper space, and context-leaving cleanup separately in AutoCAD. Do not change drawing visibility or system variables to force selection feedback.
- **The action strip crowds object details:** compare root and object states at 300 by 450 and 370 by 660, including keyboard focus and scrolling.
- **The active panel/switching change also covers lifecycle behavior:** preserve its shell/module boundaries and camera-safe restoration. Keep its outstanding native checks separate when updating specifications.

## Migration Plan

Update the existing contracts, native actions, Layers view model, and selected UI together, including Preview adapters and affected test fakes. No stored data or dependency migration is needed.

Verify the concrete regression cases above, build with zero warnings, inspect Rider warnings separately, and run the existing affected checks. Build into the normal plugin output; if AutoCAD locks it, use an alternate repository output and report that deployment is pending. A fresh host load is needed for native verification.

Rollback restores the prior code and plugin binaries as one change; there is no drawing-data migration to reverse. Archive/sync this change only after implementation verification, without silently completing the separate panel/switching change.
