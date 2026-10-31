# Lens ideas

Candidates for future CAD Lens work. Nothing here is planned or committed; these notes record the
ideas and what each one needs from the current code.

The name comes from the lenses in Sid Meier's Civilization, where a lens repaints the map to answer
one question. Today's lenses browse a list and hide objects, so the drawing itself is never painted.

## Map mode

Paint the drawing instead of adding another list. Any Group by choice gives each group a temporary
color, or a gradient for numeric properties, and group rows become the legend with visibility toggles.
Every existing property then works as a map view: length, text height, `Attribute: MARK`, and so on.

`EntityIsolationService` already holds the mechanism. A `DrawableOverrule` can set `traits.Color` in
`SetAttributes` without touching the DWG. This needs a map from object to color and the same cleanup
and regeneration rules as isolation. Check in the host: plotting, nested block contents, and regeneration
cost on large drawings.

## Lens candidates

| Lens         | Civilization analogy | Question it answers                             | Needs                                   | Effort       |
|--------------|----------------------|-------------------------------------------------|-----------------------------------------|--------------|
| Blocks       | Resources            | Where is each block used, with which attributes? | Existing block properties               | Low          |
| Health       | Builder lens         | What in this drawing needs attention?           | Rules over snapshots, a few new values  | Low to medium |
| Changes      | Timeline             | What changed since the last Refresh?            | A comparison of two detached inventories | Low          |
| Fog of War   | Fog of war           | Which model objects reach no layout viewport?   | Entity bounds, viewport reading         | Medium       |
| Weight       | Density              | Why is this DWG slow?                           | Size facts, optional density grid       | Medium       |
| Elevation    | Continents           | Which objects sit at unexpected elevations?     | Coordinates in snapshots                | Medium       |
| Dependencies | Trade routes         | Which xrefs, images and fonts does it rely on?  | A data source other than entities       | Medium       |
| Civil        | None                 | What surfaces, alignments and networks exist?   | The Civil 3D API in a separate project  | High         |

### Blocks

Root level by block name, then insert count, attribute values, and individual insertions. Also useful:
unreferenced definitions and dynamic visibility states. This works as a quick schedule.

### Health

One group per rule, each opening the matching objects. Rules are close to saved filters, so most of the
filter code can be reused. Starting rules: objects on layer 0, color and linetype overrides instead of
ByLayer, zero-length objects, nearly closed polylines, text height outliers, and proxy objects.
Geometry rules, such as duplicates and stray objects far from the rest, need coordinates in snapshots.

### Fog of War

Find model-space objects that no layout viewport shows, because they silently never reach a sheet.
Read each layout viewport's model-space window and frozen layers, then test entity bounds against them.

### Changes

Compare the retained inventory with a new read by object identity and list added, changed, and deleted
objects. This covers properties within one session; comparing two DWG files is a separate problem.

### Weight

List heavy hatches, dense polylines, and blocks with the most expanded objects. With Map mode this can
also paint a density grid over the drawing.

### Elevation

Group by Z bands and report flatness problems. `EntitySnapshot` holds no coordinates or bounds yet; adding
bounds would serve Fog of War, Health, and Elevation together.

## Suggested order

1. Blocks: the cheapest lens, and the one that forces the preparation below.
2. Health: the most useful in daily work.
3. Map mode: the feature that delivers the Civilization idea.

## Preparation for a third lens

`DrawingGrouping` is checked in many places across `DrawingLensProvider`, `ObjectExplorerViewModel`,
`ObjectExplorerLens`, and `ExplorerComposition`, for labels, the settings file, and navigation depth.
Replace those branches with one descriptor record when the third lens arrives, not earlier.
`ObjectExplorerViewModel` should also shed responsibilities at that point.