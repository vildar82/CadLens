# lens-explorer Spec Delta

## MODIFIED Requirements

### Requirement: Single compact floating panel

The explorer SHALL use one floating panel with a draggable header and a close button, with one exploration state following the active drawing. It SHALL NOT store separate selection or navigation state for each viewport. The panel SHALL provide a compact lens bar and expanded content within the same window. The lens controls SHALL be generated from registered lens modules. Layers SHALL remain the only production lens implementation, without controls for unregistered lenses. The lens control SHALL visibly distinguish active and inactive states. Dragging, closing, and the lens control SHALL remain accessible in both modes. When expanded, the panel SHALL display the active lens and current space. Opening details SHALL replace the content rather than open additional detail panels. Content that exceeds the available panel area SHALL remain reachable by scrolling.

#### Scenario: Move the panel

- **WHEN** the user drags the panel header in either mode
- **THEN** the whole panel moves without changing the exploration selection or drawing view

#### Scenario: Open details

- **WHEN** the user opens a group from the expanded list
- **THEN** its details replace the list inside the same panel, with a visible navigation path

#### Scenario: Content exceeds the panel height

- **WHEN** the expanded list contains more entries than fit in the panel
- **THEN** the user can scroll to every entry while the lens control, header, and close control remain reachable

#### Scenario: Compact lens bar

- **WHEN** no lens is active
- **THEN** the panel shows the inactive Layers control and window controls without retaining the expanded content's empty area

## ADDED Requirements

### Requirement: Lens activation and deactivation

Each registered lens control SHALL toggle between inactive compact mode and active expanded mode. Activating SHALL show the selected lens explorer without moving any drawing camera. Deactivating SHALL hide the content and remove all temporary visualization owned by the session without changing drawing geometry or properties. Pending work or automatic refresh SHALL NOT reactivate the lens, expand the panel, or restore effects while the lens is inactive. Closing SHALL remain available in either mode and SHALL end the session and remove its effects.

#### Scenario: First activation

- **WHEN** the user activates Layers in a new compact session with a drawing open
- **THEN** the control becomes active and the panel expands to the current-space layer list with the existing default inclusion settings
- **AND** no camera moves and no group is selected automatically

#### Scenario: Collapse after selecting an object

- **WHEN** the user presses the active Layers control after selecting an object
- **THEN** Layers becomes inactive, the panel becomes compact, and owned highlighting and dimming are cleared
- **AND** the camera remains at its current position, including any earlier explicit Focus result

#### Scenario: Collapse with work pending

- **WHEN** the user deactivates Layers while a read or emphasis request is pending
- **THEN** the panel becomes compact and the pending result cannot leave or reapply temporary effects or reactivate Layers

#### Scenario: Refresh while inactive

- **WHEN** the drawing is edited while Layers is inactive
- **THEN** the panel remains compact and the drawing remains free of session-owned visualization

#### Scenario: Close compact panel

- **WHEN** the user closes the compact panel
- **THEN** the window disappears, the session ends, and pending results cannot recreate its effects

### Requirement: Restore exploration within the current session

Collapsing SHALL preserve the current navigation position and inclusion settings within the open session. Reactivating in an unchanged drawing context SHALL restore that position and its applicable emphasis without moving the camera. Restoration SHALL use current valid targets: if drawing edits invalidate the saved position, the explorer SHALL return to a valid ancestor or the current root instead of acting on stale objects. Changing document or active space SHALL invalidate the saved position from the previous context. An inactive lens SHALL remain inactive across context changes. With no drawing, drawing-dependent actions SHALL remain unavailable. Closing the panel SHALL discard saved exploration state.

#### Scenario: Resume an unchanged selection

- **WHEN** the user reactivates Layers after collapsing at a selected layer, type, or object and the context and targets remain valid
- **THEN** the panel restores that position and inclusion settings and reapplies its applicable emphasis without moving the camera

#### Scenario: Resume the root

- **WHEN** the user reactivates Layers after collapsing at its root
- **THEN** the layer list returns without selection-specific highlighting or dimming

#### Scenario: Saved object was erased

- **WHEN** the saved object is erased while the panel is compact and the user reactivates Layers
- **THEN** the explorer displays current data at a valid ancestor or root and does not emphasize or focus the erased object

#### Scenario: Change context while compact

- **WHEN** the user changes the active drawing or space while Layers is inactive
- **THEN** the panel stays compact with no effects
- **AND** the next activation opens the new context's root rather than restoring the previous context's selection

#### Scenario: No active drawing

- **WHEN** the last drawing closes while the panel is compact
- **THEN** the panel remains compact and no lens action accesses the closed drawing or activates drawing-dependent content

### Requirement: Discover and switch registered lenses

Adding a lens SHALL require implementing ILens and registering the module and its dependencies in DI. Each lens SHALL own its WPF view, view model, data models, services, and commands. The shell SHALL discover its identity and label without creating its view or loading drawing data, generate its toolbar control, and display its view in a ContentControl. The shared contract SHALL be limited to identity, view, lifecycle, and context notifications. It SHALL NOT require a common inventory presentation, navigation, filters, Focus, or highlighting methods. No lens-specific change to the window, shell view model, or host owner SHALL be required.

Only one lens SHALL be active at a time. Switching SHALL cancel and settle pending work and clear the old lens's effects before loading the new lens. Failed cleanup SHALL leave the panel inactive and report the failure; a later activation attempt SHALL retry cleanup. Each lens SHALL own its session state. Layers SHALL retain its navigation and inclusion settings. Context changes SHALL invalidate all saved paths, and closing SHALL discard all lens state.

#### Scenario: Add another registered lens

- **WHEN** another self-contained lens module and its dependencies are registered in DI
- **THEN** its labeled control appears without a panel code change or startup drawing read
- **AND** selecting it displays its own XAML and runs its own view model and services

#### Scenario: Switch while a lens operation is pending

- **WHEN** the user selects a different lens during a pending read or emphasis
- **THEN** the old request is canceled and its late result cannot publish content or recreate effects
- **AND** the new lens loads only after the old work settles and cleanup succeeds

#### Scenario: Cleanup fails during switching

- **WHEN** the old lens cleanup fails or is unavailable
- **THEN** the next lens is not activated and the compact status explains the failure
- **AND** a later lens activation request retries cleanup before loading

#### Scenario: Return to a previous lens

- **WHEN** the user switches back to a previously used lens in the same drawing context
- **THEN** its module-owned state is restored; Layers refreshes its filters and valid navigation from current data
- **AND** state from the intervening lens is not applied to it

#### Scenario: Unrelated lens content

- **WHEN** a registered lens supplies a different WPF layout and commands unrelated to drawing exploration
- **THEN** the shell displays that view and its bindings without requiring Layers models or actions

#### Scenario: Module close and context notifications

- **WHEN** the drawing context changes
- **THEN** every module receives context invalidation without automatic activation
- **WHEN** the panel closes or the host terminates
- **THEN** modules cancel their own work and synchronously release effects before the shared host queue is disposed
