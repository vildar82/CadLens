# autocad-command Specification

## Purpose

The CADLENS command gives the user a minimal way to check that the plugin is loaded and available in AutoCAD 2025 or 2026.

## Requirements

### Requirement: Command availability

After the plugin is loaded in a supported AutoCAD version, the system SHALL register a command named `CADLENS`.

#### Scenario: Command is available after loading

- **WHEN** the user loads the plugin in AutoCAD 2025 or 2026 and enters `CADLENS` on the command line
- **THEN** AutoCAD runs the plugin command

### Requirement: English greeting without drawing changes

When `CADLENS` runs, the system SHALL print an English greeting containing `CAD Lens` on the AutoCAD command line, finish without further user input, and leave the open DWG unchanged.

#### Scenario: Command runs with a drawing open

- **WHEN** the user runs `CADLENS` with a DWG open
- **THEN** an English greeting containing `CAD Lens` appears on the command line, the command finishes, and the DWG remains unchanged