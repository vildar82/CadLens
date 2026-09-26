# Spec Delta

## MODIFIED Requirements

### Requirement: Temporary emphasis and dimming

With Auto Highlight enabled by default, selecting a layer SHALL emphasize its drawable objects and dim other drawable objects from the current active-space inventory across all views of the active drawing where those objects are visible. The same target set SHALL apply across viewports, respecting actual visibility in each viewport. Selecting a type SHALL narrow emphasis to that type within the selected layer. Selecting an individual object SHALL emphasize that object. With Auto Highlight off, navigation SHALL clear previous temporary effects without applying new emphasis; Highlight SHALL apply emphasis to the current group or object on demand without enabling Auto Highlight. Turning Auto Highlight off or pressing Reset SHALL clear emphasis and dimming. Highlighting and its cleanup SHALL NOT move cameras or change CAD selection, except that shared Reset also clears CAD selection. Inclusion in counts SHALL NOT override actual visibility: off or frozen objects SHALL remain hidden, remain available in included counts and navigation, and have their hidden status explained in details. Focus and Select SHALL NOT unhide them. Temporary visualization SHALL NOT modify object or layer properties.

Target emphasis SHALL combine an accent color with an additional visible cue so that a target remains distinguishable when its original color already matches the accent. The cue SHALL preserve recognizable geometry and hatch content.

A highlighted patterned hatch SHALL retain its pattern, scale, angle, and openings. Highlighting SHALL NOT replace the pattern with a solid fill. A highlighted solid hatch SHALL remain recognizable as a filled region with its original openings.

Non-target inventory hatches SHALL be dimmed together with non-target linework. Dimming SHALL reduce the visual prominence of hatch pattern strokes, any hatch background fill, and solid fills relative to emphasized targets, while retaining visible drawing context. It SHALL NOT introduce filled areas into hatch openings.

Clearing temporary effects SHALL restore the original display appearance, including hatch patterns and fills, without changing stored geometry or properties.

#### Scenario: Select a layer and then a type

- **WHEN** Auto Highlight is enabled and the user selects Roads and then its Polylines group
- **THEN** the visible Roads objects are initially emphasized, followed by only its visible polylines, with other drawable objects dimmed

#### Scenario: Shared emphasis across viewports

- **WHEN** Auto Highlight is enabled, Auto Focus is off, and two viewports show the same model-space objects as the user selects a layer, type, or object in CAD Lens
- **THEN** those objects are emphasized and other inventory objects are dimmed in both viewports wherever they are visible, without changing either camera
- **AND** objects hidden in either viewport remain hidden there, and clearing or closing removes the effect from both viewports

#### Scenario: Inspect included hidden geometry

- **WHEN** the user navigates to an object on an included off or frozen layer
- **THEN** its layer and hidden status appear in the details without changing visibility or promising a visible highlight; Focus can use valid bounds but does not reveal the object
- **AND** selection does not change its layer visibility or the active drawing layer

#### Scenario: Restore a broader selection

- **WHEN** Auto Highlight is enabled and the user returns from an individual object to its type or layer
- **THEN** emphasis expands to the visible objects in that ancestor selection and no stale individual highlight remains

#### Scenario: Highlight manually while automatic highlighting is off

- **WHEN** Auto Highlight is off and the user presses Highlight
- **THEN** temporary emphasis applies to the current target without changing CAD selection, cameras, or Auto settings
- **AND** navigating to a different target clears that temporary effect without applying a new one

#### Scenario: Turn off highlighting while Auto Select remains on

- **WHEN** the user turns Auto Highlight off while Auto Select is enabled
- **THEN** temporary emphasis and dimming are cleared while CAD selection, camera, and Auto Select remain unchanged
- **AND** subsequent navigation updates selection without applying temporary highlighting

#### Scenario: Highlight an object already using the accent color

- **WHEN** a visible line or polyline whose original color matches the accent becomes a highlight target
- **THEN** an additional visible cue distinguishes it from its ordinary appearance, while its geometry remains recognizable

#### Scenario: Preserve a patterned hatch during emphasis

- **WHEN** a patterned hatch with a non-default scale and angle and an internal opening becomes a highlight target
- **THEN** its pattern, scale, angle, and opening remain visible, with accent color and an additional emphasis cue
- **AND** no solid highlight fill replaces the pattern or fills the opening

#### Scenario: Emphasize a solid hatch

- **WHEN** a solid hatch with an internal opening becomes a highlight target
- **THEN** it remains recognizable as a filled region, with accent color and an additional emphasis cue
- **AND** its opening remains unfilled by the highlight effect

#### Scenario: Dim patterned hatches with a background fill

- **WHEN** visible non-target inventory entities include linework and dense patterned hatches with background fills
- **THEN** the linework, hatch pattern strokes, and hatch background fills all recede relative to emphasized targets
- **AND** the hatches remain visible as drawing context and their openings remain unfilled by the dimming effect

#### Scenario: Dim a large solid hatch

- **WHEN** a large solid hatch is a non-target inventory entity alongside visible target linework
- **THEN** its fill is visually subdued relative to the emphasized linework while remaining visible as drawing context

#### Scenario: Move emphasis between hatches

- **WHEN** the highlight target changes from one visible hatch to another
- **THEN** the previous target loses its emphasis cue and becomes dimmed, and the new target becomes emphasized
- **AND** both hatches retain their original pattern or solid-fill character and openings

#### Scenario: Clear hatch effects in all visible viewports

- **WHEN** patterned and solid hatches are emphasized or dimmed in multiple visible viewports and highlighting is cleared or the exploration session ends
- **THEN** every affected viewport shows their original appearance without leftover emphasis cues or dimming
- **AND** stored entity and layer properties remain unchanged
