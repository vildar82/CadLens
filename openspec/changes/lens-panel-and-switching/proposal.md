# Proposal

## Why

CAD Lens currently opens directly into the Layers explorer. Introduce a compact lens panel as the main entry point so users can activate exploration when needed and return drawing space to normal without closing the session.

## What Changes

- Use one floating window containing a compact lens bar and expandable exploration content. Layers is the only available lens; do not show placeholders for future lenses.
- Start each new CADLENS session in compact mode, with Layers inactive and no temporary visualization. Repeated invocation brings the existing window forward while preserving its state.
- Make the Layers button a toggle: pressing it activates the lens and expands its content; pressing it again deactivates the lens and collapses the content.
- Clear owned highlighting and dimming when collapsing. Remember the navigation position within the current session; reopening restores that position and its applicable emphasis without moving the camera. Restoration remains subject to current drawing context and valid targets.
- Keep the existing Layers filters, counts, navigation, details, and explicit Focus inside the expanded content. Existing document/space boundaries and stale-target protections continue to apply.
- Keep dragging and closing available in both modes. Closing ends the session and removes its temporary effects.
- Give the panel a compact, game-inspired appearance with a clearly distinguishable active lens and responsive interaction feedback. Exact styling and transition details belong to the design stage.

## Capabilities

### New Capabilities

None. Extend the existing explorer and command capabilities.

### Modified Capabilities

- `lens-explorer`: Add the compact lens bar, lens activation/deactivation, expandable content, and navigation restoration within a session.
- `autocad-command`: Open new sessions compact with no active lens, while retaining existing-panel reuse on repeated invocation.

## Impact

- Update the WPF explorer window and view model to represent compact/inactive and expanded/active states.
- Coordinate activation, cancellation, refresh, and existing host visualization cleanup so inactive sessions cannot regain effects from pending work.
- Update focused behavior tests and user documentation; verify compact/expanded interaction and cleanup in AutoCAD.
- Reuse the current Layers provider and host graphics adapter. This change does not introduce another lens or require a new dependency.

## Non-goals

- Geometry or other additional lenses, placeholder lens controls, or a general lens plugin framework.
- Separate lens windows, simultaneous active lenses, or independent per-viewport sessions.
- Repairing known hatch/block rendering limitations or redesigning the graphics adapter.
- Persisting navigation across panel closure or AutoCAD restarts.