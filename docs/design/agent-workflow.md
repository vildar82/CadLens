# CAD Lens design agents

## WPF comparison workflow

When a design cycle is requested, use temporary isolated WPF prototypes without AutoCAD. Reuse
production view models and commands with matching sample data and scenarios. Do not add a permanent
preview project or duplicate host logic. Figma is optional and used only if explicitly requested again;
old files remain historical references.

Keep experimental views/resources separate from CadLens.UI until a variant is selected for production
integration. The designer owns assigned experimental views; the developer owns assigned prototype
support. Never give both agents the same writable files. If variants share a launcher, keep comparison
controls outside the candidate window so sizing, dragging, and resizing remain realistic. Switching
must close/dispose the previous session and cancel pending work before starting at the same scenario
baseline. Comparison controls never enter the AutoCAD plugin.

## Scope and context

These project roles support an explicitly requested design cycle. They do not start a background
service or run every role for ordinary edits. The coordinator owns the brief, handoffs, and decisions.
The role files inherit model, reasoning, and runtime permissions. Role boundaries are instructions,
not security isolation. This desktop session may expose only generic spawning: in that case, read
the selected role file and pass its developer_instructions explicitly to the spawned agent. Report
that fallback rather than claiming the custom role was automatically loaded.

Read current AGENTS.md, README.md, and the source and tests relevant to the requested behavior.
Verify facts against current source; screenshots can become stale. Begin with
src/CadLens.UI/Views/ExplorerWindow.xaml and src/CadLens.UI/Lenses/ObjectExplorer/Views/ObjectExplorerView.xaml.

The current product is one movable window over an AutoCAD drawing, with a compact inactive lens bar
and an expanded explorer. Preserve drawing space, readable CAD names, explicit Focus-only camera
movement, inclusion-only filters, and temporary effects that clean up across lifecycle transitions.
Use the existing standard WPF controls and local themes as the technical baseline. Product changes
require explicit agreement; visual alternatives may vary hierarchy, spacing, typography, and placement.

## Roles

| Role | Input | Deliverable |
| --- | --- | --- |
| ux_reviewer | Current UI, scenarios, behavior | Prioritized problems with evidence and acceptance criteria |
| visual_designer | Brief, UX findings, current UI | Runnable WPF variants, screenshots, and interaction details |
| wpf_developer | Prototype task or selected variant | Temporary WPF prototype or production integration |
| ui_reviewer | Brief or selected variant, implementation, criteria | Independent findings and verification gaps |

## Coordinator workflow

1. Establish the current visual baseline and the user's concern. Delegate UX diagnosis. The coordinator
   can inspect implementation constraints in parallel. Give each agent a bounded task and stop condition.
2. Consolidate one short brief: user goal, scenarios, constraints, evidence, and acceptance criteria.
   Pass relevant files and findings, not the entire conversation by default. Do not create a document
   hierarchy for a small task; the brief may stay in the task until an artifact is useful.
3. Assign two comparable WPF variants with matching scenarios and explicit file ownership. A requested
   WPF concept experiment includes temporary prototype code; do not wait for a winner before making
   variants runnable.
   For the current experiment, preserve A's preferred narrow structure and explore richer restrained treatments.
4. Build, launch, inspect, and independently review the variants. Show launch/switch instructions,
   variant names, screenshots, and specific interaction tasks. Let the user try them in one app.
5. Once the user selects a design, give the developer the selected variant, scope, criteria, and
   exact file ownership for production integration. A running prototype does not select a design;
   no separate planning artifacts are required.
6. Give the reviewer the brief or selected design and evidence without coaching it toward a pass.
   Route required fixes to the developer, then recheck affected scenarios. After two unsuccessful
   correction rounds, summarize the unresolved tradeoff for the user instead of cycling indefinitely.
7. Report what changed, what was actually verified, and which AutoCAD/DPI/graphics checks remain.

Every assignment should specify: objective, context paths, variant/scenario names, allowed writes,
non-goals, expected output, and stop condition. Agents return evidence and uncertainty, not only opinions.
Use parallel agents for independent work only. Do not spawn all roles simply because they exist.

## Optional Figma destination

The user selected their personal team, shown by Figma as “Вильдар Хисяметдинов's team”,
plan key `team::1302647684591686884`. Do not use the Test team. This selection applies to this project's
design work if Figma is explicitly requested again; reuse it without asking again. If unavailable, report the blocker rather
than choosing another destination. Load the mandatory Figma skills before their corresponding tools.
The previous concept file is historical; there is no dependency on its completion or tool quota.

## Starting a cycle

Example request:

> Compare two visual treatments of A in a temporary WPF prototype. Keep both selectable in one app.
> Show compact, list, and object states with real hover/press feedback before production integration.

For production integration, name the selected variant. For review, provide the brief or chosen variant and
current implementation evidence. Roles can also be invoked separately for a narrow task.

## Instruction sources and evaluation

- [OpenAI custom subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents): project TOML
  roles, narrow responsibilities, and inherited settings. Required fields are name, description,
  and developer_instructions. No model override is necessary for this experiment.
- [Anthropic multi-agent engineering](https://www.anthropic.com/engineering/multi-agent-research-system):
  explicit delegation contracts, bounded effort, evidence handoffs, and evaluating real outcomes.
- [Anthropic frontend-design](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md):
  product-specific visual direction and a deliberate critique pass. Adapted as principles, not installed
  or copied wholesale: website hero sections and decorative novelty are not goals for a CAD overlay.

The local .NET/WPF and review skills provide tool-specific procedures when applicable.
A role name alone does not supply the product brief or prove quality. Evaluate the first cycle on
specific outcomes: distinct usable concepts, preserved behavior, inspected visuals, clear handoff,
and a reviewer that reports evidence and gaps. Adjust instructions in response to observed failures.

TOML role files use UTF-8 without BOM for standard TOML parser compatibility. Other text files retain
the repository encoding. Syntax validation does not prove native role discovery or design quality.