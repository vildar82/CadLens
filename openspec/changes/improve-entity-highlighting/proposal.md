# Proposal

## Why

Color-only dimming does not reliably affect hatch backgrounds or block contents. In the diagnostic drawing, changing `DrawableTraits.TrueColor` or `Transparency` on one hatch or block insertion had no visible effect, while `DrawableAttributes.IsInvisible` hid the whole object. Recoloring detached hatch clones and exploded block copies adds complexity and cannot faithfully cover all block behavior.

## What Changes

- Replace Highlight and Auto Highlight with temporary Isolate and Auto Isolate actions.
- Draw the selected active-space objects with their original appearance and suppress other direct active-space objects through a `DrawableOverrule` visibility flag.
- Clear the overrule on Reset, root navigation, context change, lens collapse, and session close; regenerate the affected drawing views.
- Remove hatch-clone rendering, block explosion, color options, and their experimental commands.
- Keep DWG entity and layer properties, CAD selection, and cameras unchanged by isolation.

## Capabilities

### Modified Capabilities

- `layers-lens`: Replace temporary emphasis and dimming with visual isolation.
- `lens-explorer`: Rename the independent Highlight control and Auto mode to Isolate while keeping their navigation and cleanup lifecycle.

## Impact

- The renderer becomes a filtered `DrawableOverrule` that marks non-target active-space entities invisible during display.
- Target hatches retain their native patterns and backgrounds. A hidden block insertion is treated as one entity, so its nested graphics do not require traversal or explosion.
- Native AutoCAD validation remains necessary for nested blocks, attributes, xrefs, viewports, selection, object snaps, plotting, and cleanup.

## Non-goals

- Persisting `Entity.Visible` changes or changing layer visibility.
- Calling AutoCAD's global `ISOLATEOBJECTS`/`UNISOLATEOBJECTS` commands.
- Retaining dimmed context or custom target colors.
