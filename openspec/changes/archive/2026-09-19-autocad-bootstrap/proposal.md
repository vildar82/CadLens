# Proposal

## Why

Start CAD Lens with a small working plugin that is easy to understand and verify in AutoCAD before adding lenses and UI.

## What Changes

- Add `src/AutoCAD/CadLens.AutoCAD` to `CadLens.slnx`.
- Add a `CADLENS` command that prints a greeting in AutoCAD.
- Lenses, UI, ribbon integration, and an installer are outside this change.

## Capabilities

### New Capabilities

- `autocad-command`: run `CADLENS` and print a greeting for the user.

### Modified Capabilities

None.

## Impact

A new AutoCAD plugin project and an updated solution file. Build, loading, and in-host verification were to be decided in the next planning steps.