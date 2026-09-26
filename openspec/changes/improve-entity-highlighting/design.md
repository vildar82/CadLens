# Design

## Context

`EntityHighlightService` changed ambient drawing color, but the user's hatch and block test showed no visible change from `DrawableTraits.TrueColor` or `Transparency`. The `IsInvisible` return flag did hide both a direct hatch and a block insertion. The clone-and-explode path is therefore unnecessary for the chosen visual-isolation behavior.

## Decision

Keep the existing active-space inventory and target calculation. In `EntityIsolationService`, filter the overrule to non-target inventory `ObjectId`s. Call the base `SetAttributes` and add `DrawableAttributes.IsInvisible` to its returned flags. Target objects are not overruled; AutoCAD draws their hatches, blocks, attributes, and nested geometry normally. A non-target block insertion is hidden as a whole, without visiting its definition.

Do not write `Entity.Visible`, alter layer records, clone hatches, or explode blocks. Keep the current context checks before applying a target set. Remove the overrule and regenerate all viewports on Reset, root navigation, panel collapse/close, and drawing-context changes. Preserve the Auto mode lifecycle and independence from CAD selection and Focus.

## Validation

The single-object probe establishes only that the flag hides a hatch and a block insertion in the user's drawing. Validate repeated layer/type/object navigation, patterned and solid hatches, blocks with attributes or nested blocks, xrefs, two insertions of one definition, multiple viewports, and cleanup after document switches. Check whether visually hidden objects can still be selected or snapped and whether plotting while isolation is active uses the temporary display. If those behaviors conflict with the product meaning of Isolate, revise the interaction before treating this as complete.

## Migration

The UI uses Isolate and Auto Isolate. Internal color options and both rendering probes are removed. No drawing migration is needed because the overrule does not change stored entity or layer properties.
