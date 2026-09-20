# Spec Delta

## Purpose

Provide a compact drawing-centered interface for exploring a lens through groups, details, and individual objects while preserving the user's drawing.

## ADDED Requirements

### Requirement: Single compact floating panel

The explorer SHALL use one floating panel with a draggable header and a close button, with one exploration state following the active drawing. It SHALL NOT store separate selection or navigation state for each viewport. Opening details SHALL replace the panel's previous content rather than open additional detail panels. The panel SHALL display the active lens and current space. Content that exceeds the available panel area SHALL remain reachable by scrolling.

#### Scenario: Move the panel

- **WHEN** the user drags the panel header
- **THEN** the panel moves without changing the exploration selection or drawing view

#### Scenario: Open details

- **WHEN** the user opens a group from the list
- **THEN** its details replace the list inside the same panel, with a visible navigation path

#### Scenario: Content exceeds the panel height

- **WHEN** the list contains more entries than fit in the panel
- **THEN** the user can scroll to every entry while the header and close control remain reachable

### Requirement: Navigation between exploration levels

The explorer SHALL provide breadcrumbs and Back navigation. Returning to an ancestor SHALL restore that level's content and visual emphasis, clearing emphasis belonging only to the deeper selection.

#### Scenario: Return from a type to its parent group

- **WHEN** the user goes Back from a selected object type
- **THEN** the parent group's type breakdown and whole-group emphasis are restored

#### Scenario: Return to the root

- **WHEN** the user selects the root breadcrumb
- **THEN** the group list appears and selection-specific emphasis and dimming are cleared

### Requirement: Individual object navigation

The explorer SHALL provide Previous, Next, and a one-based position counter for the current object set. Browsing SHALL update the selected object's emphasis and preview without moving the drawing view. At the first and last objects, unavailable navigation directions SHALL be disabled. The order SHALL remain stable while the underlying result set is unchanged.

#### Scenario: Browse objects

- **WHEN** the user selects Next at object 7 of 180
- **THEN** object 8 of 180 becomes current, its preview and emphasis replace the previous object's, and the view remains unchanged

#### Scenario: Reach an endpoint

- **WHEN** the first or last object is current
- **THEN** Previous or Next respectively is disabled; both are disabled for a single-object set

### Requirement: Explicit view focus

Selecting groups, types, and objects SHALL leave the view unchanged. A separate Focus action SHALL fit the current group or object in the active view only when usable bounds exist; other viewport cameras SHALL remain unchanged. Missing or invalid bounds SHALL produce a clear unavailable state or explanation without moving the view or failing the session.

#### Scenario: Focus a group

- **WHEN** the user activates Focus for a group with valid bounds
- **THEN** the view fits that group's bounds while preserving the current exploration level

#### Scenario: Bounds are unavailable

- **WHEN** a focus target has no usable bounds
- **THEN** the user receives an explanation and the view remains unchanged

### Requirement: Lens-specific information

The active lens SHALL determine the information presented in object previews. A group selection SHALL remain local to that lens's exploration and SHALL NOT create an application-wide object restriction.

#### Scenario: Preview with Layers active

- **WHEN** an object preview is displayed while Layers is active
- **THEN** the preview identifies the object's layer, without presenting the selected layer as a global filter

### Requirement: Session cleanup and document boundaries

Closing the explorer SHALL remove its temporary highlights, dimming, and previews without changing stored geometry or properties. Switching documents or active spaces SHALL clear old selection and effects before presenting results for the new context. Actions SHALL NOT operate on objects from an inactive or closed document. With no active drawing, drawing-dependent content and actions SHALL be unavailable.

#### Scenario: Close after exploration

- **WHEN** the user presses the close button after selecting and focusing objects
- **THEN** the panel and its temporary visuals disappear and normal drawing appearance is restored, without undoing the user's explicit view navigation

#### Scenario: Change the active context

- **WHEN** the user switches document or active space while exploring
- **THEN** old effects and selections are cleared and the panel returns to the active lens's root for the new context

#### Scenario: Close the last drawing

- **WHEN** the active drawing closes and no drawing remains active
- **THEN** its temporary effects are released and no drawing-dependent action remains available