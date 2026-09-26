# autocad-command Spec Delta

## MODIFIED Requirements

### Requirement: Open the explorer without drawing changes

When CADLENS runs with a drawing open in AutoCAD 2025 or 2026 and no panel exists, the system SHALL open a new session in compact mode with Layers inactive and no temporary highlighting or dimming. The command SHALL return control to AutoCAD without requiring command-line input and SHALL leave DWG geometry, properties, and camera position unchanged.

#### Scenario: Command runs with a drawing open

- **WHEN** the user runs CADLENS with a DWG open and no panel exists
- **THEN** the compact lens bar opens with Layers inactive and no visualization effects
- **AND** the command finishes without changing DWG geometry, properties, or the camera

#### Scenario: Reopen after closing

- **WHEN** the user closes an expanded explorer and runs CADLENS again
- **THEN** a new compact session opens with Layers inactive and no previous navigation selection restored

### Requirement: Repeated invocation

The system SHALL reuse the existing explorer for the active document when CADLENS is invoked again, without creating duplicate panels or temporary effects. Bringing the panel forward SHALL preserve its compact or expanded mode, activation state, and valid exploration state.

#### Scenario: Explorer is already open

- **WHEN** the user runs CADLENS again in the same document
- **THEN** the existing panel is brought forward with its exploration state preserved

#### Scenario: Existing panel is compact

- **WHEN** the user runs CADLENS while the existing panel is compact
- **THEN** that panel is brought forward without activating Layers, expanding content, or applying effects

#### Scenario: Existing panel is expanded

- **WHEN** the user runs CADLENS while Layers is active
- **THEN** that panel is brought forward without resetting navigation or creating another session