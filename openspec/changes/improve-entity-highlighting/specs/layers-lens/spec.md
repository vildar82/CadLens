# Spec Delta

## REMOVED Requirements

### Requirement: Temporary emphasis and dimming

**Reason**: Hatch backgrounds and explicit block contents do not reliably follow the drawing-color and transparency traits. Faithful per-object dimming would require rendering copies and block reconstruction that are disproportionate to this feature.

**Migration**: The Highlight and Auto Highlight controls become Isolate and Auto Isolate. Targets retain their normal appearance while other active-space objects are temporarily hidden.

## ADDED Requirements

### Requirement: Temporary visual isolation

With Auto Isolate enabled, selecting a layer SHALL keep its direct active-space objects visible and temporarily suppress the display of other direct active-space objects. Selecting a type SHALL narrow the visible set to that type within the selected layer; selecting an object SHALL narrow it to that object. The same target set SHALL apply across viewports without moving cameras. Targets SHALL retain their native geometry, colors, hatch patterns and backgrounds, block contents, and existing visibility restrictions. Off or frozen objects SHALL remain hidden and available in included counts and navigation.

With Auto Isolate off, navigation SHALL clear previous isolation without applying new isolation. Isolate SHALL apply it to the current group or object on demand without enabling Auto Isolate. Turning Auto Isolate off, returning to the root, pressing Reset, leaving the drawing context, or closing the session SHALL restore the ordinary display in every affected viewport. Isolation SHALL NOT change stored entity or layer properties, CAD selection, or cameras; shared Reset also clears CAD selection.

#### Scenario: Navigate from a layer to an object

- **WHEN** Auto Isolate is enabled and the user selects a layer, then one of its types, then one object
- **THEN** each step replaces the previous visible target set with the narrower direct active-space set, and previously isolated objects resume their normal appearance when included again

#### Scenario: Preserve a hatch and a block insertion

- **WHEN** a patterned hatch or block insertion is a target
- **THEN** AutoCAD draws its original pattern, background, attributes, and nested content without CAD Lens recreating those graphics
- **AND** a non-target hatch or block insertion is suppressed as one active-space object

#### Scenario: Manual isolation and cleanup

- **WHEN** Auto Isolate is off and the user presses Isolate, then presses Reset or leaves the lens
- **THEN** the current targets are isolated on demand and the ordinary drawing display is restored on cleanup without changing saved visibility properties

#### Scenario: Existing hidden objects

- **WHEN** a target is on an off or frozen layer
- **THEN** it remains hidden, its status is explained in details, and CAD Lens does not turn on the layer or promise a visible target

#### Scenario: Multiple viewports

- **WHEN** two viewports show the same active-space objects and isolation is applied or cleared
- **THEN** both viewports use the same target set and return to their original appearance after cleanup, with their cameras unchanged
