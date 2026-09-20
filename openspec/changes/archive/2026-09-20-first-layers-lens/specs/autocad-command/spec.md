# Spec Delta

## REMOVED Requirements

### Requirement: English greeting without drawing changes

**Reason**: Opening the explorer replaces the bootstrap greeting as confirmation that CAD Lens is running. The user intentionally removed the greeting.

**Migration**: Run CADLENS to open or activate the panel; no greeting is required. Geometry and property preservation remain required below.

## ADDED Requirements

### Requirement: Open the explorer without drawing changes

When `CADLENS` runs with a drawing open in AutoCAD 2025 or 2026, the system SHALL open the explorer with the Layers lens active, and return control to AutoCAD without requiring command-line input. The command SHALL leave DWG geometry and properties unchanged.

#### Scenario: Command runs with a drawing open

- **WHEN** the user runs `CADLENS` with a DWG open
- **THEN** the Layers explorer opens for the active space, and the command finishes without changing DWG geometry or properties

### Requirement: Repeated invocation

The system SHALL reuse the existing explorer for the active document when `CADLENS` is invoked again, without creating duplicate panels or temporary effects.

#### Scenario: Explorer is already open

- **WHEN** the user runs `CADLENS` again in the same document
- **THEN** the existing panel is brought forward with its exploration state preserved