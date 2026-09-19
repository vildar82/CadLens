# Spec Delta

## MODIFIED Requirements

### Requirement: English greeting without drawing changes

When `CADLENS` runs with a drawing open in AutoCAD 2025 or 2026, the system SHALL print an English greeting containing `CAD Lens`, open the explorer with the Layers lens active, and return control to AutoCAD without requiring command-line input. The command SHALL leave DWG geometry and properties unchanged.

#### Scenario: Command runs with a drawing open

- **WHEN** the user runs `CADLENS` with a DWG open
- **THEN** an English greeting containing `CAD Lens` appears, the Layers explorer opens for the active space, and the command finishes without changing DWG geometry or properties

## ADDED Requirements

### Requirement: Repeated invocation

The system SHALL reuse the existing explorer for the active document when `CADLENS` is invoked again, without creating duplicate panels or temporary effects.

#### Scenario: Explorer is already open

- **WHEN** the user runs `CADLENS` again in the same document
- **THEN** the existing panel is brought forward with its exploration state preserved