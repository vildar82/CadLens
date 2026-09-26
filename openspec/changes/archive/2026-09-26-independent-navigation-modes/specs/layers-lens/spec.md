# Spec Delta

## MODIFIED Requirements

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
