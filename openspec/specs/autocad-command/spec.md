# autocad-command Specification

## Purpose

The CADLENS command opens or activates the Layers explorer in AutoCAD 2025 or 2026 without changing drawing geometry or properties.

## Requirements

### Requirement: Command availability

After the plugin is loaded in a supported AutoCAD version, the system SHALL register a command named `CADLENS`.

#### Scenario: Command is available after loading

- **WHEN** the user loads the plugin in AutoCAD 2025 or 2026 and enters `CADLENS` on the command line
- **THEN** AutoCAD runs the plugin command

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