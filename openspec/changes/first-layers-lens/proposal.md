# Proposal

## Why

CAD Lens currently only confirms that the plugin is loaded. The first Layers lens will help users understand an unfamiliar drawing while testing whether a compact, game-inspired interface makes exploration useful and enjoyable.

## What Changes

- Extend `CADLENS` to open a compact floating panel. A draggable header and close button control the panel; closing it clears temporary visualization and restores the normal drawing appearance.
- Show layers containing objects in the active space, with total object counts. Use model space in Model and inside an activated layout viewport; use paper space while the sheet itself is active. Panning and zooming do not change the scope or counts.
- Provide independent snowflake and lightbulb toggles to include frozen and switched-off layers and their objects in the list and counts. Both start off. Locked layers are included normally. These controls do not change layer properties.
- Count each block insertion as one object on its assigned layer, without exploring its contents.
- Support the path Layers -> layer -> primitive type -> individual object. Show counts per type and object details on demand. Breadcrumbs and Back restore the preceding level and its emphasis.
- Emphasize the chosen layer's objects and dim the other inventory objects temporarily across all views of the active drawing where those objects are visible. Use one exploration session, without separate viewport state. Choosing a primitive type narrows emphasis within that layer; choosing an individual object updates its highlight and preview.
- Keep the view unchanged during selection and Previous/Next navigation. Provide an object counter and explicit Focus actions to fit the current group or object on screen.
- Let the active lens determine the information shown in object previews. Layers previews emphasize layer information; a selected layer does not become a global restriction on other lenses.
- Use one panel whose details replace its list. Explore a visual direction combining the tactical mockup's compact layout and clear controls with the strategy mockup's softer corners. Accent colors remain open for experimentation. Use WPF UI for the UI foundation.

## Capabilities

### New Capabilities

- `layers-lens`: Active-space layer discovery, inclusion filters, counts, type and object navigation, layer-focused previews, and temporary visual emphasis.
- `lens-explorer`: Shared floating exploration panel, navigation between levels, contextual information, explicit focus actions, and closing the exploration session.

### Modified Capabilities

- `autocad-command`: Extend the greeting-only command contract to launch the explorer while preserving command availability and leaving DWG geometry and properties unchanged.

## Impact

- Extend the AutoCAD command and add host integration for reading objects, resolving references, highlighting/dimming, view navigation, and document/space lifetime.
- Introduce a Core project for lens contracts and shared exploration infrastructure, independent of Layers, AutoCAD, and WPF.
- Introduce a separate UI project that renders generic groups, counts, details, and actions without layer-specific logic or AutoCAD references. Keep Layers semantics in the feature implementation and AutoCAD API access in host integration.
- Add the WPF UI dependency through the root dependency configuration. Exact project boundaries and package compatibility will be resolved in design.
- Verify the interface and temporary visualization inside AutoCAD; mockups establish visual direction but do not demonstrate host behavior or performance. Frozen/off object visualization and viewport-specific visibility need explicit treatment in specs and design.

## Non-goals

- Whole-document or screen-visible-only scope, configurable scope selection, and block-content traversal.
- Editing objects or layer properties, changing layer visibility, and filtering other lenses by the selected layer.
- Implementing additional lenses or a general-purpose UI framework beyond what this first slice needs.