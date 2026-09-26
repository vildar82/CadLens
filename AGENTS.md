# CAD Lens Rules

## Rule 1: Minimum necessary code

- Act like a pragmatic, lazy developer: solve only the current requirement with the smallest amount of clear, working code.
- Do not build abstractions, metadata, wrappers, or infrastructure for hypothetical future needs.
- Prefer deleting unnecessary concepts to renaming them or distributing them across more classes.
- A host task service must execute queued work directly; do not nest one execution service inside another.

## Language

- Use English in project instructions, documentation, specifications, code comments, and user-facing text.
- Use English for GitHub Issues, Projects, milestones, and pull requests for this repository.
- Discuss this project in English, including voice conversations when voice chat is available.

## Pull requests

- Keep each PR focused on one coherent change. Put unrelated fixes, cleanup, and experiments in separate PRs.
- Use a specific English title that describes the result and reads well in release notes. Avoid generic titles such as "Fix", "Update", or "Bundle".
- Describe the final diff: why the change is needed, what it changes, and how it was verified. Link the relevant issue or OpenSpec change when there is one.
- Report managed checks and native AutoCAD/Civil 3D observations separately. Do not include unverified claims, unrelated work, or a running implementation diary.
- Before merging, review the final diff, title, description, and CI result. Update the PR text if its scope changed.

## Releases

- Use the effective `CadLens.AutoCAD` project `Version` from `Directory.Build.props`. Bump it only when the intended release contents are ready to merge into `main`; do not reuse a published version for changed binaries.
- The `release` job in `.github/workflows/build.yml` publishes a prerelease after a successful `main` build when `v<Version>` does not exist. Normal releases need no release branch, manually created tag, or GitHub release form.
- GitHub generates release notes from merged PR titles and links the full commit history. Make PR titles accurate before merging; individual commit messages do not appear in the description by default.
- Check the published tag, `CadLens.bundle.zip`, release notes, and documented verification limits. CI does not verify behavior inside AutoCAD or Civil 3D.

## Namespaces

- In C# projects, use only the project's root namespace. Do not add folder names to namespaces.
- Disable Rider's Namespace Provider for code folders in the project's `.csproj.DotSettings`. When adding a new folder, do this alongside its first C# file.

## Warnings

- Fix compiler, analyzer, and Rider warnings, including `is never used` on entry points invoked by AutoCAD through attributes.
- Apply `UsedImplicitly` or `PublicAPI` annotations only to classes and methods that are genuinely used implicitly. Do not disable inspections globally for these cases.
- After changing C# code, build the solution with zero warnings. Check Rider warnings separately because some are not reported by the build.

## Dependencies

- Keep NuGet package versions and references in the root `Directory.Build.props`.
- Set `UseAutocad=true` in each `.csproj` that needs the AutoCAD API. The root `Directory.Build.props` adds the package based on this property. Other projects must not reference the AutoCAD package.
- Make `JetBrains.Annotations` available to all projects through the root `Directory.Build.props`.

## Readability

- Write simple, self-documenting code: names and extracted methods must explain intent without a wall of comments.
- Split large methods into short, named steps with one responsibility. Keep orchestration separate from details.
- Separate logical blocks with blank lines, especially around conditions, loops, and before returning a result.
- Keep all parameters or arguments on one line, or put every parameter or argument on its own line. Never mix both styles.
- Hide host API details (handle parsing, object lookup flags, and context transitions) behind clearly named extensions or services.
- Use one host task service that owns its queue and performs the native context transition itself.
- Prefer List<T> for local collection construction. Use immutable collections for published snapshots when callers must not change their contents.

## Design agents

- For a requested window design cycle, use the focused roles in `.codex/agents/` and follow
  [the design agent workflow](docs/design/agent-workflow.md). Delegate independent work only.
- Keep ordinary edits local; do not start the full design cycle unless requested.
- Compare runnable WPF variants in one `CadLens.Preview` application by default; Figma is optional.
- Keep experimental styling in the preview until a variant is selected for production integration.
- Role TOML files use UTF-8 without BOM for TOML parser compatibility.
