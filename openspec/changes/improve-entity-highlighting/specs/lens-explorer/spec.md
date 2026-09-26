# Spec Delta

## MODIFIED Requirements

### Requirement: Independent drawing controls

The expanded Layers explorer SHALL show Focus, Select, and Isolate in three columns, each with its own Auto toggle, followed by one shared Reset. The controls SHALL remain visible at every exploration level. Each manual action SHALL change only its own camera, CAD selection, or temporary-isolation state without changing Auto settings. Auto states and keyboard focus SHALL be visually distinguishable. Manual actions SHALL be unavailable without a current target; Auto settings SHALL remain configurable at the initial layer list. A new session SHALL start with Auto Focus off, Auto Select off, and Auto Isolate on.

#### Scenario: Configure modes before opening a layer

- **WHEN** the initial layer list appears in a new session
- **THEN** all three Auto controls are visible with Focus off, Select off, and Isolate on, while manual actions are disabled
- **AND** enabling Auto Select changes the setting without selecting the whole drawing or moving the camera

#### Scenario: Run an independent manual action

- **WHEN** the user presses Focus, Select, or Isolate for a current target
- **THEN** only that action's drawing state changes, even if another action previously used a different target
- **AND** Auto settings and the current exploration level remain unchanged

#### Scenario: Use the narrow panel

- **WHEN** the expanded panel is 300 by 450 device-independent pixels
- **THEN** the action strip and Reset remain reachable without scrolling the content, and the content can be scrolled independently
- **AND** keyboard navigation gives the focused action or Auto toggle a visible outline

### Requirement: CAD object selection

Select and enabled Auto Select SHALL replace CAD selection with valid objects from the current group, type, or object in the active drawing space. The drawing SHALL show the updated selection without requiring focus to leave the modeless panel. Selection SHALL NOT move the camera, change temporary isolation, or depend on usable focus bounds or an unlocked viewport. Invalid or deleted targets SHALL be excluded. If no valid targets remain, the action SHALL clear stale CAD selection and explain that selection is unavailable. With Auto Select off, ordinary navigation SHALL leave existing CAD selection unchanged; root navigation and session cleanup SHALL still clear it. Selection SHALL NOT modify stored geometry or properties or reveal objects hidden by their own visibility or layer state.

#### Scenario: Select without usable focus

- **WHEN** a valid object has no usable focus bounds or the active viewport is locked, and the user presses Select
- **THEN** the object is selected without moving the camera or changing temporary isolation

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

Each Auto mode SHALL be independent and apply its action to the current target immediately when enabled. Subsequent navigation SHALL apply all enabled modes to the destination target. An unavailable focus operation SHALL NOT prevent selection or isolation from running. Turning Auto Select off SHALL clear CAD selection only; turning Auto Isolate off SHALL clear temporary isolation only; turning Auto Focus off SHALL leave the camera unchanged. Auto settings SHALL persist through root navigation and collapse/reactivation within the session. Reset SHALL turn all Auto modes off. Restoring a collapsed session SHALL NOT invoke Auto Focus.

#### Scenario: Enable an Auto mode for the current target

- **WHEN** the user enables an Auto mode while a target is current
- **THEN** that action runs immediately, and the other actions and Auto settings remain unchanged

#### Scenario: Disable an Auto mode

- **WHEN** the user turns Auto Select, Auto Isolate, or Auto Focus off
- **THEN** only selection or temporary isolation is cleared as applicable, and no camera restoration occurs

#### Scenario: Focus cannot run during automatic navigation

- **WHEN** Auto Focus, Auto Select, and Auto Isolate are enabled and the next valid target has no usable focus bounds
- **THEN** the view stays unchanged with an explanation, while selection and isolation still update to that target

### Requirement: Shared drawing reset

Reset SHALL clear CAD selection and temporary isolation, and turn off all Auto modes. It SHALL preserve the camera, current navigation, and inclusion filters. Reset SHALL be disabled while work is pending, so a completed Reset cannot be overwritten by an older operation. The explorer SHALL provide no separate Clear isolation or Isolate CAD Selection button.

#### Scenario: Reset with automatic modes enabled

- **WHEN** the user presses Reset after all pending work finishes
- **THEN** selection and isolation are cleared, all Auto modes are off, and the camera, navigation, and filters remain unchanged
- **AND** the effects stay cleared during navigation until an Auto mode is enabled or a manual action runs

#### Scenario: Navigate after Reset

- **WHEN** the user navigates to another layer, type, object, or ancestor after Reset
- **THEN** selection, isolation, and camera remain unchanged while all Auto modes are off

#### Scenario: Work is still pending

- **WHEN** a drawing action or inventory load is pending
- **THEN** Reset is unavailable until that work finishes
