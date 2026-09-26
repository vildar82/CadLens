# Proposal

## Why

`EntityHighlightService` currently emphasizes targets and dims other inventory entities by overriding their display color. Dimming works acceptably for lines but poorly for hatches, and color alone does not make targets distinct enough. Hatch emphasis must preserve the visible pattern so users can still recognize the drawing's content.

## What Changes

- Improve temporary target emphasis beyond an accent color, keeping geometry readable in a busy drawing.
- Preserve highlighted hatch patterns, including their scale, angle, and openings; do not replace patterned hatches with solid highlight fills.
- Make non-target hatches visually recede alongside other non-target inventory entities, including large solid fills and dense patterns.
- Keep effects temporary and preserve stored entity and layer properties, actual visibility, CAD selection, and camera positions.
- Preserve shared effects across visible viewports and restore the original appearance when highlighting is cleared or the exploration session ends.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `layers-lens`: Strengthen the temporary emphasis and dimming requirement with perceptible target emphasis, hatch pattern preservation, and effective non-target hatch dimming.

## Impact

- Primary implementation area: `src/Common.AutoCAD/Services/EntityHighlightService.cs`. Rendering options may need a small extension after the visual approach is agreed.
- Existing Highlight and Auto Highlight actions continue to supply the target and inventory sets; this proposal does not change navigation or selection behavior.
- Native AutoCAD checks will be needed for patterned and solid hatches, dense patterns, openings, overlapping geometry, multiple viewports, and cleanup. Source inspection alone cannot establish the cause of the reported hatch rendering problem or prove a replacement works.

## Open Decisions

- Choose the additional emphasis cue. A stronger stroke for linework and an accent boundary around a hatch are initial candidates, not an approved rendering design.
- Clarify how hatch dimming currently fails: unchanged foreground pattern, dominant background or solid fill, or loss of pattern readability. Use that observation to choose the rendering approach.

## Non-goals

- Changing hatch definitions, stored colors, transparency, lineweights, or layer properties.
- Changing CAD selection behavior, adding highlight settings UI, or introducing a general rendering framework.
