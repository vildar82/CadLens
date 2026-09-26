# Design

## Context

See [proposal.md](proposal.md) for motivation and [spec.md](specs/layers-lens/spec.md) for behavior. `EntityHighlightService` currently changes only `SubEntityTraits.TrueColor`. In the diagnostic drawing, 687 of 691 direct patterned hatches have a separate background color, and 312 further hatches are in block definitions outside the direct active-space inventory.

A one-hatch AutoCAD probe rendered a detached hatch clone with independently dimmed pattern and background colors. The user confirmed that the pattern remained visible and the result looked suitably subdued. This validates the visual direction for one direct hatch, not performance or lifecycle behavior across the whole drawing.

## Goals / Non-Goals

**Goals:**

- Apply the proven foreground-and-background treatment to direct active-space hatches without changing stored hatch properties.
- Preserve the hatch's own pattern, scale, angle, boundary loops, and openings by rendering a clone rather than reconstructing geometry.
- Give targets a second visible cue without changing CAD selection.
- Dim non-target block references as whole insertions, including their visible nested graphics, without affecting another insertion of the same definition.

**Non-Goals:**

- Edit shared block definitions or explode resident block references in the drawing.
- Use object transparency for dimming; the diagnostic drawing has `TRANSPARENCYDISPLAY=0`.
- Keep the experimental `CADLENSHATCHPROBE` command after production integration.

## Decisions

### Render direct hatches through detached clones

Keep one `EntityHighlightService` and its current direct-entity filter. For each direct hatch in the active inventory, clone the hatch in a host read transaction before registering or refreshing the overrule. Set the clone's pattern color to the target accent or ordinary dim color. Set a non-target clone's background to a darker dim color, with enough contrast to keep the pattern legible. A target clone also uses a subdued background so its accent pattern remains readable. Forward the hatch's `WorldDraw` and, when needed, `ViewportDraw` to that clone; continue using `SetAttributes` for other entity types.

The probe demonstrated this for one hatch. Changing only ambient `TrueColor` leaves the hatch background independent, while changing the real hatch's `BackgroundColor` would modify the drawing. A generic translucent overlay would also cover hatch openings. Cloning retains AutoCAD's own hatch renderer and geometry.

Prepare clones outside graphics callbacks. Keep them alive while the overrule can draw them; after removing the overrule and regenerating affected viewports, dispose them. Rebuild clones when the inventory is refreshed so edits to source hatches are reflected. If preparing one clone fails, leave that hatch on the existing color-only path and report the failure during host validation rather than changing the DWG.

### Use AutoCAD's native highlight as the extra target cue

After the overrule regeneration, call `Entity.Highlight` for target entities in all viewports. Unhighlight the previous targets before changing the target set and on Reset, context change, or session close. This adds a host-provided cue without custom boundary reconstruction or changes to CAD selection. Test its appearance on lines, solid hatches, and patterned hatches before accepting the full change; if it obscures hatch content, revise this decision rather than layering a solid overlay over the hatch.

### Block rendering is a required per-insertion validation gate

Continue treating each active-space block reference as one inventory entity, but do not treat a color change on that reference as proof that its contents dim. AutoCAD lets nested objects retain explicit colors, and a block definition can be shared by several insertions with different target states. Recoloring definition entities globally would therefore give the wrong result.

Before production integration, test a single selected block reference that contains explicitly colored geometry and a hatch. The probe must affect only that occurrence and preserve its attributes, dynamic state, clipping, nested blocks, and native visibility. Inspect the active drawing's block occurrences to choose representative cases, including an xref if present. A detached `Explode` rendering is only a possible experiment for simple blocks: AutoCAD documents that exploding can lose attribute values and cannot handle xrefs, so it is not the general design. If no per-occurrence rendering path preserves these behaviors, stop and revise the design with the user rather than ship block references as silently undimmed context.

## Risks / Trade-offs

- **Many hatch clones per navigation** → Measure the effect in the diagnostic drawing with 837 direct hatches. Add caching only if the simple per-application rebuild is too slow.
- **Graphics lifetime or viewport-specific drawing differs from the one-hatch probe** → Test repeated navigation, multiple viewports, Reset, document switching, and panel close before removing the probe.
- **Native highlight competes with the hatch pattern or CAD selection feedback** → Validate it visually on target hatches and linework; revise the cue if it hides content or looks like CAD selection.
- **Nested block graphics retain explicit colors** → Treat per-insertion dimming as a completion gate; do not change shared definitions or substitute an exploding path that loses block behavior.

## Migration Plan

After hatch and block rendering are validated, replace the temporary probe with production graphics code and delete the probe command and its application cleanup hook. No drawing migration is needed. A rollback removes the new drawing path; no hatch, block, or layer properties are persisted.
