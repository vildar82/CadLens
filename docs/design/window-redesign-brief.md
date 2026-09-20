# CAD Lens window redesign: first concept trial

Status: WPF concept exploration. The user prefers A's narrow structure but wants more visual character
and satisfying feedback. Compare preview-only variants in one CadLens.Preview app before production integration.

## User goal

Make both compact and expanded CAD Lens feel responsive, understandable, and satisfying to use.
The desired delight comes from predictable interaction and thoughtful feedback, not bright colors,
decoration, or distracting animation. The user does not need to prescribe a visual style upfront.

## Design task

Create two restrained WPF treatments of A using the same content and tasks, selectable in one application.
Give the user concrete interaction examples to compare. Preserve the drawing as the main workspace.
Explain each concept's practical advantage and cost; avoid asking which screenshot looks cooler.

Current source baseline: 300 x 52 compact; 370 x 660 initial expanded; one movable window; Segoe UI;
WPF-UI controls and custom templates. These are starting points, not newly mandated design dimensions.
The README screenshot predates the current shell. Current rendered WPF and native mouse behavior
have not been inspected during this concept trial; source findings must be labeled accordingly.

## Shared scenarios

- Find and activate Layers from the compact bar; identify the draggable area and Close.
- Scan a layer list with long names and counts; hover, press, and open one full-width row.
- Navigate layer -> primitive type -> object, then return without losing orientation.
- Inspect the object and move to Previous/Next; use Focus deliberately to move the camera.
- Understand inclusion filters and distinguish exploration emphasis from highlighting CAD preselection.
- Recognize reading, no results, unavailable Focus, and cleanup pending/failed; collapse and close.

## Interaction criteria

- Hover identifies the target without changing selection, the camera, or the drawing.
- Pressed, active, keyboard-focus, and disabled states are distinct; color is not the only cue.
- Activation is acknowledged immediately even if drawing work is pending. Never fake host completion.
- Navigation retains clear location and a reachable return action; full names remain accessible.
- Essential actions remain discoverable without hover. Contextual feedback must not shift row layout.
- Motion explains a user action and is brief and interruptible. Propose timings as design values,
  not verified performance. Respect reduced motion; never delay cleanup or input for animation.
- Keep visual noise and drawing occlusion low. Compare both concepts at equal scale on the same backdrop.

## Source findings for this trial

- Custom templates have incomplete pressed feedback. The active Layers checked state overrides its
  hover background; list rows lack explicit pressed, keyboard-focus, and disabled treatments.
- Repeated headings and the prominent CAD preselection action compete with the browsing content.
- Compact cleanup status relies on small symbols and tooltips while activation may be unavailable.
- Snowflake/lightbulb inclusion controls can be mistaken for actual layer visibility commands.
- Focus and Previous/Next follow variable object details, so their positions can change.

Evidence: ExplorerWindow.xaml, LayersView.xaml, ExplorerViewModel.cs, and LayersViewModel.cs in
src/CadLens.UI. These are source-backed design risks, not observed native usability test results.

## Boundaries

Keep a single movable window and the currently registered Layers lens. Do not introduce search,
favorites, additional lenses, drawing edits, or automatic zoom as part of a visual concept.
Preserve inclusion-only filters, explicit Focus, inactive-mode behavior, and cleanup/document boundaries.
Any interaction that changes the existing behavioral contract must be labeled as a proposal.

## Agent assignments and deliverables

UX reviewer: inspect current source, identify up to five specific interaction problems, and provide evidence.
Visual designer: own assigned preview-only WPF views with matched compact/list/group/object states,
real mouse/keyboard feedback, and loading/empty/error examples. Inspect running views before handoff.
WPF developer: own shared variant selection and scenario/session support, with separate file ownership.
Coordinator: reconcile constraints and findings, present tradeoffs, and help the user compare tasks.
UI reviewer: check concept coverage and clarity before presentation; report visual evidence limits.
Production integration starts after the user chooses a variant and the project's planning steps are agreed.

## Review with the user

Ask which concept makes it easier to notice the next action, understand a click, read the current context,
and return to the drawing. The user may prefer parts of each or reject both. No expert design vocabulary
is needed, and choosing a screenshot does not validate actual WPF interaction quality.

## Historical Figma trial (superseded by WPF)

Personal team only. Concept file: https://www.figma.com/design/4FmvideADGr4ilx7UpUPJ9
Specific frame links and prototype limitations will be added after visual verification.
Segoe UI is unavailable in the connected Figma font service. Inter is an intentional proposed font
for these new concepts, not a claim of fidelity to the current UI. An implementation must resolve
font availability and metrics before the concept can be called visually matched.

## First trial result

Figma MCP reached the personal team's Starter call limit before the comparison was finished.
Do not treat this file as a complete or accepted design. No application code changed.

- A Quiet rail: [inspected root list](https://www.figma.com/design/4FmvideADGr4ilx7UpUPJ9?node-id=6-37),
  370 x 660. Compact frame 6:35 is 300 x 52. Root preview shows eight rows. The saved root screenshot
  predates a Close/collapse ordering correction; that correction has not been re-rendered.
- B Context desk: [inspected root list](https://www.figma.com/design/4FmvideADGr4ilx7UpUPJ9?node-id=6-54),
  460 x 620. Compact frame 6:52 is 280 x 60. Wider master/detail layout is a proposal, with less width
  for individual layer names despite the larger overall window.
- [Action states](https://www.figma.com/design/4FmvideADGr4ilx7UpUPJ9?node-id=4-14): Rest, Hover,
  Pressed, Focus, Disabled, Active. Browse row state family: 6:26. Rest-to-hover reactions use a
  proposed 100 ms dissolve; press acknowledgment is immediate. Player behavior has not been tested.
- A group/object/reading/empty/error frames 6:39, 6:41, 6:43, 6:45, 6:47 were populated but not
  visually verified; object layout may overflow. Equivalent B frames 6:56, 6:58, 6:60, 6:62, 6:64
  contain headers only. Compact pending/error examples remain missing. Screen navigation is unwired.

Do not resume Figma work by default. Retain these links and limits as historical evidence.
Continue with interactive WPF variants in the standalone preview application.

Independent concept review recommends A as the initial drawing-first direction: its list is more readable
and its window occupies less area. B's persistent context benefit remains unverified in deeper states.
Required copy correction before the next presentation: replace ambiguous Clear with Clear highlight.
This correction could not be applied after the MCP limit. Neither concept has passed interaction review.