# Verification

## Implemented

Variant B is integrated into the production Layers view. Focus changes the camera, Select changes CAD selection, and Highlight changes temporary emphasis. Each has its own Auto setting; Reset turns off those settings and keeps the camera.

Selection uses the shared editor extension and existing host queue. Navigation and lifecycle handling stay in the existing Layers view model. Preview uses the same mode commands; the duplicate mode wrapper was removed.

## Managed checks — September 26, 2026

- `dotnet build CadLens.slnx -c Debug --no-restore`: passed, zero warnings and errors.
- `dotnet test CadLens.slnx --no-restore --no-build`: 97 passed, zero failed or skipped (Common 4, Lenses 8, managed AutoCAD helpers/queue 12, UI 73).
- `openspec validate independent-navigation-modes --strict` and `git diff --check`: passed.
- Regression coverage includes selection-only navigation at every level, independent manual actions and toggles, Reset/resumption, unavailable Focus, restoration without camera movement, and cancellation during pending selection.
- Review caught initial compact startup clearing external preselection. Cleanup is now recorded only after activation and remains owed after failed collapse. Tests cover both inactive preselection preservation and close/context cleanup after failure.
- The new Focus result appears first in navigation status, so a camera failure is not hidden behind successful selection/highlight messages.

The plugin built successfully into `src/AutoCAD/CadLens.AutoCAD/bin/Debug/net8.0-windows/CadLens.AutoCAD.dll`. No alternate output was needed. Build output is not proof that an already-running AutoCAD loaded the new assemblies.

## WPF evidence

- `artifacts/modes-integrated-final/checks.txt`: all seven mode, cancellation, restoration, and rendering checks passed.
- Actual WPF captures cover both comparison variants at 300 by 450 and 370 by 660, compact mode at 300 by 52, and root/layer/type/object/empty/error/busy states. B uses the production Layers view. Keyboard-focus and scrolled-object captures were inspected.
- `artifacts/modes-focus-unavailable/action-strip-focus-unavailable-300.png`: the complete locked-viewport Focus reason is visible in the narrow status area. The full status and tooltip binding were checked. This is a text fixture, not a native viewport test.
- Generated images and logs are ignored by Git. Recreate them with `--capture-modes <directory>`; add `--focus-unavailable` for the targeted status fixture. Use `--modes` for the interactive comparison or no arguments for the ordinary production-layout preview.

Rider inspections could not be run: no inspection tool was available through PATH or the connected tools. Build warnings are verified; Rider-only warnings are not. Native DPI, dragging/resizing, and AutoCAD graphics/selection behavior remain unverified.

## AutoCAD checks for the user

Load the rebuilt plugin in a fresh host session and record the host/version and outcomes:

1. With objects already selected, open CADLENS compact; preselection should remain. Activate Layers, turn Auto Select on and Auto Focus off, then browse layer/type/object, Previous/Next, Back, and breadcrumbs. Selection should follow without camera movement.
2. Try each manual action independently. Turn Auto Highlight off while keeping Auto Select on; only the temporary effect should clear. Press Reset with modes enabled, then navigate; effects should clear and remain off until a mode is enabled again.
3. In a locked viewport, Select should work while Focus reports unavailable. Check erased targets, included hidden layers, model/paper space, and multiple viewports. Hidden objects must stay hidden, with no stored geometry, property, or layer-visibility changes.
4. Collapse and reopen with all modes enabled; selection/highlight should restore without moving the camera. Close during pending work, switch documents/spaces, and close the last drawing; no old effects should return. Failed collapse cleanup must still be retried on close or context change.

Tasks 5.1 and 5.2 remain unchecked until these native results are supplied. This change is not archived or synced into the main specs. The separate panel/switching change and its outstanding checks are unchanged.
