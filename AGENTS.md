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