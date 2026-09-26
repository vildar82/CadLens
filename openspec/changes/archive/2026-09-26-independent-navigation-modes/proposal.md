# Proposal

## Why

Focus currently moves the camera and selects objects together. Users need to browse layers, types, and objects with selection updating while the camera stays at the drawing overview.

The user selected preview variant B, **Action strip**, for production integration.

## What Changes

- Use B's three compact columns: Focus, Select, and Highlight, each with its own Auto toggle below it. Keep the controls visible from the initial layer list through object details.
- Make Focus change only the camera, Select change only CAD selection, and Highlight change only temporary emphasis and dimming. Manual actions remain independent; Select remains available when camera focus is unavailable.
- Apply each enabled Auto mode when opening layers, types, or objects and when using Previous, Next, Back, or an ancestor breadcrumb. Auto Select works with Auto Focus off.
- Allow Auto settings to be changed at the initial layer list. With no current target, disable manual actions. Returning to that list clears selection and highlight without moving the camera or changing Auto settings.
- Enabling an Auto mode applies it immediately when a target exists. Turning Auto Select or Auto Highlight off clears its effect; turning Auto Focus off leaves the camera in place.
- Use the preview's initial settings: Auto Focus off, Auto Select off, Auto Highlight on.
- Replace Clear highlight with one Reset action. Reset clears CAD selection and temporary highlight, turns off all Auto modes, and preserves navigation and camera. Disable Reset while work is pending.
- Use B's compact, vertically centered Back arrow and retain readable, reachable breadcrumbs and object details.
- Preserve cleanup and cancellation across collapse, close, and drawing-context changes so stale work cannot restore effects. Keep hidden objects hidden and preserve stored drawing geometry and properties.

## Capabilities

### New Capabilities

None. Extend the existing exploration capabilities.

### Modified Capabilities

- `lens-explorer`: Separate camera focus from CAD selection; define independent automatic navigation actions, shared Reset, and consistent controls at every exploration level.
- `layers-lens`: Coordinate optional CAD selection and temporary emphasis for the current layer, type, or object while preserving visibility and inventory rules.

## Impact

- Integrate the selected Action strip from `CadLens.Preview` into the existing Layers view and view model.
- Update Layers drawing actions and AutoCAD visualization operations. Reuse the shared selection extension and existing host task queue.
- Update affected specifications, documentation, and focused regression checks for independent actions, Reset, navigation, and cleanup.
- Keep the comparison launcher in Preview. No new dependency, lens, or action framework is needed. AutoCAD behavior requires separate native verification.
