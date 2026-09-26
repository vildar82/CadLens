# layers-lens Specification

## Purpose
Help users understand the active drawing space through layer counts, primitive-type breakdowns, object navigation, and temporary visual emphasis.

## Requirements

### Requirement: Active-space layer inventory

The Layers lens SHALL list layers with at least one included object belonging directly to the active space, showing each layer's name and object count. Model space SHALL be used in Model and inside an activated layout viewport. Paper space of the active layout SHALL be used while the sheet itself is active. Counts SHALL include objects outside the current view and SHALL NOT change solely because of panning or zooming. Layers with no included objects SHALL be omitted.

#### Scenario: Explore Model

- **WHEN** a layer has 12 model-space objects, 5 of which are outside the current view
- **THEN** its count is 12 and remains 12 when the user pans or zooms without changing the drawing

#### Scenario: Explore a sheet

- **WHEN** the sheet itself is active and a layer contains 3 objects on that sheet and 12 in model space
- **THEN** its count is 3; model objects seen through viewports and objects on other sheets are not included

#### Scenario: Enter a layout viewport

- **WHEN** the user activates model space inside a layout viewport
- **THEN** the inventory uses all included model-space objects, not only those inside the viewport's visible bounds

#### Scenario: No included objects

- **WHEN** the active space and inclusion settings yield no objects
- **THEN** the panel shows an empty state explaining that no layers contain included objects in the current space

### Requirement: Frozen and switched-off inclusion controls

The lens SHALL provide independent Include frozen and Include off toggles, represented by snowflake and lightbulb controls with explanatory labels or tooltips. Both SHALL start off when a new explorer session opens. A layer frozen globally or in the active layout viewport SHALL require Include frozen; a switched-off layer SHALL require Include off. A layer with both conditions SHALL require both toggles. Locked status SHALL NOT exclude a layer. Changing a toggle SHALL update the inventory and counts without changing layer properties, and clear a selection that is no longer included.

#### Scenario: Default exclusions

- **WHEN** a new session opens with visible, frozen, switched-off, and locked layers containing objects
- **THEN** visible and visible locked layers are included, while frozen and switched-off layers are excluded

#### Scenario: Independent conditions

- **WHEN** Include frozen is enabled and Include off remains disabled
- **THEN** frozen layers that are on are included, while all switched-off layers remain excluded

#### Scenario: Layer has both conditions

- **WHEN** a layer is both frozen and switched off
- **THEN** it is included only while both inclusion toggles are enabled

#### Scenario: Viewport-specific freezing

- **WHEN** a model-space layer is frozen only in the currently activated layout viewport
- **THEN** Include frozen controls its inclusion for that context; this viewport condition does not exclude it in the Model tab

#### Scenario: Remove the current layer from scope

- **WHEN** disabling an inclusion toggle excludes the selected layer
- **THEN** its selection and temporary effects are cleared and the panel returns to the updated layer list

### Requirement: Counts and primitive-type breakdown

Selecting a layer SHALL show its total count and counts grouped by primitive type. Each included object SHALL contribute once to the total and to exactly one type group. Each block insertion SHALL count as one object on its assigned layer; its contents SHALL NOT be traversed or counted separately. Selecting a type SHALL provide access to its individual objects.

#### Scenario: Type totals agree

- **WHEN** a layer contains 180 polylines, 48 lines, and 20 arcs
- **THEN** its total is 248 and the type groups show 180, 48, and 20 respectively

#### Scenario: Block insertion

- **WHEN** a layer contains two block insertions whose definitions contain additional geometry
- **THEN** those insertions contribute two objects to that layer and one block-insertion type group, regardless of their internal entity count or layers

### Requirement: Temporary emphasis and dimming

With Auto Highlight enabled by default, selecting a layer SHALL emphasize its drawable objects and dim other drawable objects from the current active-space inventory across all views of the active drawing where those objects are visible. The same target set SHALL apply across viewports, respecting actual visibility in each viewport. Selecting a type SHALL narrow emphasis to that type within the selected layer. Selecting an individual object SHALL emphasize that object. With Auto Highlight off, navigation SHALL clear previous temporary effects without applying new emphasis; Highlight SHALL apply emphasis to the current group or object on demand without enabling Auto Highlight. Turning Auto Highlight off or pressing Reset SHALL clear emphasis and dimming. Highlighting and its cleanup SHALL NOT move cameras or change CAD selection, except that shared Reset also clears CAD selection. Inclusion in counts SHALL NOT override actual visibility: off or frozen objects SHALL remain hidden, remain available in included counts and navigation, and have their hidden status explained in details. Focus and Select SHALL NOT unhide them. Temporary visualization SHALL NOT modify object or layer properties.

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

### Requirement: Layer-oriented object preview

An individual object's preview in the Layers lens SHALL identify its assigned layer and primitive type. Previewing or navigating an object SHALL NOT change the active layer used by AutoCAD for drawing new objects.

#### Scenario: Inspect a polyline

- **WHEN** the current object is a polyline assigned to Roads
- **THEN** its preview identifies Roads and Polyline, and AutoCAD's current drawing layer remains unchanged
