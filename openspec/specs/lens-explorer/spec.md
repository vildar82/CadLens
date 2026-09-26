# lens-explorer Specification

## Purpose
Provide a compact drawing-centered interface for exploring a lens through groups, details, and individual objects while preserving the user's drawing.

## Requirements

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

### Requirement: Navigation between exploration levels

The explorer SHALL provide readable breadcrumbs and a compact Back button with a vertically centered arrow. Opening a group or type, going Back, or choosing an ancestor breadcrumb SHALL update the content and apply enabled Auto modes to the destination. With Auto Highlight enabled, ancestor navigation SHALL replace deeper emphasis with the ancestor's emphasis. Returning to the root SHALL clear CAD selection, temporary emphasis, and dimming without moving the camera or changing Auto settings. The root SHALL NOT implicitly target the whole drawing.

#### Scenario: Return from a type to its parent group

- **WHEN** the user goes Back from a selected object type with Auto Highlight enabled
- **THEN** the parent group's type breakdown and whole-group emphasis are restored
- **AND** CAD selection and the camera update only if their corresponding Auto modes are enabled

#### Scenario: Select while keeping the drawing overview

- **WHEN** only Auto Select is enabled and the user opens a layer, opens a type, then uses Back or an ancestor breadcrumb
- **THEN** CAD selection follows each destination while the drawing view remains unchanged and no temporary highlighting is applied

#### Scenario: Return to the root

- **WHEN** the user selects the root breadcrumb or goes Back to the root
- **THEN** the group list appears and CAD selection, emphasis, and dimming are cleared
- **AND** the camera and Auto settings remain unchanged

### Requirement: Individual object navigation

The explorer SHALL provide Previous, Next, and a one-based position counter for the current object set. Opening an object or using Previous or Next SHALL update its preview and apply each enabled Auto mode to that object. With Auto Focus off, browsing SHALL leave the drawing view unchanged. At the first and last objects, unavailable navigation directions SHALL be disabled. The order SHALL remain stable while the underlying result set is unchanged.

#### Scenario: Browse objects

- **WHEN** Auto Highlight is enabled, Auto Focus is off, and the user selects Next at object 7 of 180
- **THEN** object 8 of 180 becomes current, its preview and emphasis replace the previous object's, and the view remains unchanged
- **AND** CAD selection updates only if Auto Select is enabled

#### Scenario: Browse with selection only

- **WHEN** only Auto Select is enabled and the user opens an object or selects Previous or Next
- **THEN** CAD selection follows the current object without moving the camera or applying temporary highlighting

#### Scenario: Reach an endpoint

- **WHEN** the first or last object is current
- **THEN** Previous or Next respectively is disabled; both are disabled for a single-object set

### Requirement: View focus

Selecting groups, types, and objects SHALL leave the view unchanged unless Auto Focus is enabled. A separate Focus action and enabled Auto Focus SHALL fit the current group or object in the active view only when usable bounds exist; other viewport cameras SHALL remain unchanged. Focus SHALL NOT change CAD selection or temporary highlighting. Missing or invalid bounds, a locked viewport, or an unsupported view SHALL produce a clear unavailable state or explanation without moving the view or failing the session. Focus SHALL NOT reveal hidden objects.

#### Scenario: Focus a group

- **WHEN** the user activates Focus for a group with valid bounds in a supported view
- **THEN** the view fits that group's bounds while preserving the current exploration level, CAD selection, and temporary highlighting

#### Scenario: Bounds are unavailable

- **WHEN** a focus target has no usable bounds
- **THEN** the user receives an explanation and the view remains unchanged
- **AND** Select and Highlight remain usable for valid targets

### Requirement: Lens-specific information

The active lens SHALL determine the information presented in object previews. A group selection SHALL remain local to that lens's exploration and SHALL NOT create an application-wide object restriction.

#### Scenario: Preview with Layers active

- **WHEN** an object preview is displayed while Layers is active
- **THEN** the preview identifies the object's layer, without presenting the selected layer as a global filter

### Requirement: Session cleanup and document boundaries

Collapsing or closing the explorer SHALL clear CAD selection, temporary highlights, dimming, and previews without changing stored geometry or properties or restoring the camera. Collapse SHALL preserve navigation, inclusion filters, and Auto settings. Reactivation SHALL restore valid navigation against fresh data, falling back to a valid ancestor or root if needed, and apply enabled Auto Select and Auto Highlight without moving the camera even when Auto Focus is enabled. Failed cleanup SHALL retain the existing inactive/retry behavior. Switching documents or active spaces SHALL clear old selection and effects before presenting results for the new context and discard the old navigation path. An inactive lens SHALL remain compact. Pending work SHALL be canceled or ignored so it cannot restore old effects after cleanup. Actions SHALL NOT operate on objects from an inactive or closed document. With no active drawing, drawing-dependent content and actions SHALL be unavailable. Closing SHALL discard the session settings.

#### Scenario: Close after exploration

- **WHEN** the user presses the close button after selecting and focusing objects
- **THEN** the panel disappears, CAD selection and its temporary visuals are cleared, and normal drawing appearance is restored without undoing the user's view navigation

#### Scenario: Collapse and reopen with Auto Focus enabled

- **WHEN** the user collapses and reopens the lens with all Auto modes enabled and the saved target remains valid
- **THEN** collapse clears selection and highlighting, and reopening restores navigation, selection, and highlighting
- **AND** Auto settings remain enabled, the camera stays unchanged, and Auto Focus resumes on subsequent navigation

#### Scenario: Change the active context

- **WHEN** the user switches document or active space while exploring
- **THEN** old effects and selections are cleared and the panel returns to the active lens's root for the new context without moving the camera
- **AND** a lens that was inactive remains compact with its old navigation discarded

#### Scenario: Old work finishes after cleanup

- **WHEN** work for the previous target finishes after collapse, close, or a drawing-context change
- **THEN** it cannot restore old selection, highlighting, navigation, or camera movement

#### Scenario: Close the last drawing

- **WHEN** the active drawing closes and no drawing remains active
- **THEN** its temporary effects are released and no drawing-dependent action remains available

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

### Requirement: Independent drawing controls

The expanded Layers explorer SHALL use the selected Action strip layout: Focus, Select, and Highlight in three columns, each with its own Auto toggle below it, followed by one shared Reset. These controls SHALL stay visible at the layer list, group, type, and object levels. Each manual action SHALL change only its own camera, CAD selection, or temporary-highlight state, without changing Auto settings. Auto states and keyboard focus SHALL be visually distinguishable. Manual actions SHALL be unavailable without a current target; Auto settings SHALL remain configurable at the initial layer list. A new session SHALL start with Auto Focus off, Auto Select off, and Auto Highlight on.

#### Scenario: Configure modes before opening a layer

- **WHEN** the initial layer list appears in a new session
- **THEN** all three Auto controls are visible with Focus off, Select off, and Highlight on, while manual actions are disabled
- **AND** enabling Auto Select changes the setting without selecting the whole drawing or moving the camera

#### Scenario: Run an independent manual action

- **WHEN** the user presses Focus, Select, or Highlight for a current target
- **THEN** only that action's drawing state changes, even if another action previously used a different target
- **AND** Auto settings and the current exploration level remain unchanged

#### Scenario: Use the narrow panel

- **WHEN** the expanded panel is 300 by 450 device-independent pixels
- **THEN** the action strip and Reset remain reachable without scrolling the content, and the content can be scrolled independently
- **AND** keyboard navigation gives the focused action or Auto toggle a visible outline

### Requirement: CAD object selection

Select and enabled Auto Select SHALL replace CAD selection with valid objects from the current group, type, or object in the active drawing space. The drawing SHALL show the updated selection without requiring focus to leave the modeless panel. Selection SHALL NOT move the camera, change temporary highlighting, or depend on usable focus bounds or an unlocked viewport. Invalid or deleted targets SHALL be excluded. If no valid targets remain, the action SHALL clear stale CAD selection and explain that selection is unavailable. With Auto Select off, ordinary navigation between targets SHALL leave existing CAD selection unchanged; root navigation and session cleanup SHALL still clear it. Selection SHALL NOT modify stored geometry or properties or reveal hidden objects.

#### Scenario: Select without usable focus

- **WHEN** a valid object has no usable focus bounds or the active viewport is locked, and the user presses Select
- **THEN** the object is selected without moving the camera or changing temporary highlighting

#### Scenario: Select from the modeless panel

- **WHEN** the user presses Select while the CAD Lens panel has focus
- **THEN** the selection appears in the drawing without clicking the AutoCAD window

#### Scenario: Keep a manual selection while browsing

- **WHEN** Auto Select is off, the user selects a layer's objects manually, and then opens a type within it
- **THEN** the CAD selection remains the manually selected layer objects until another selection action or cleanup replaces it

#### Scenario: Selection target was deleted

- **WHEN** the current target contains no remaining valid objects and Select runs
- **THEN** stale CAD selection is cleared, an explanation is shown, and the session remains usable

### Requirement: Automatic drawing actions

Each Auto mode SHALL be independent and apply its action to the current target immediately when enabled. Subsequent navigation SHALL apply all enabled modes to the destination target. An unavailable focus operation SHALL NOT prevent selection or highlighting from running. Turning Auto Select off SHALL clear CAD selection only; turning Auto Highlight off SHALL clear temporary emphasis and dimming only; turning Auto Focus off SHALL leave the camera unchanged. Auto settings SHALL persist through root navigation and collapse/reactivation within the session. Reset SHALL turn all Auto modes off. Restoring a collapsed session SHALL NOT invoke Auto Focus.

#### Scenario: Enable an Auto mode for the current target

- **WHEN** the user enables an Auto mode while a target is current
- **THEN** that action runs immediately, and the other actions and Auto settings remain unchanged

#### Scenario: Disable an Auto mode

- **WHEN** the user turns Auto Select, Auto Highlight, or Auto Focus off
- **THEN** only selection or temporary highlighting is cleared as applicable, and no camera restoration occurs

#### Scenario: Focus cannot run during automatic navigation

- **WHEN** Auto Focus, Auto Select, and Auto Highlight are enabled and the next valid target has no usable focus bounds
- **THEN** the view stays unchanged with an explanation, while selection and highlighting still update to that target

### Requirement: Shared drawing reset

Reset SHALL clear CAD selection and temporary emphasis and dimming, and turn off all Auto modes. It SHALL preserve the camera, current navigation, and inclusion filters. Reset SHALL be disabled while work is pending, so a completed Reset cannot be overwritten by an older operation. The explorer SHALL provide no separate Clear highlight or Highlight CAD Selection button.

#### Scenario: Reset with automatic modes enabled

- **WHEN** the user presses Reset after all pending work finishes
- **THEN** selection and highlighting are cleared, all Auto modes are off, and the camera, navigation, and filters remain unchanged
- **AND** the effects stay cleared during navigation until an Auto mode is enabled or a manual action runs

#### Scenario: Navigate after Reset

- **WHEN** the user navigates to another layer, type, object, or ancestor after Reset
- **THEN** selection, highlighting, and camera remain unchanged while all Auto modes are off

#### Scenario: Work is still pending

- **WHEN** a drawing action or inventory load is pending
- **THEN** Reset is disabled until the work settles
