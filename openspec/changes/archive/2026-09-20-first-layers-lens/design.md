# Design

## Context

See [proposal.md](proposal.md) for motivation and the three capability deltas under `specs/` for observable behavior. The solution currently contains one `net8.0-windows` AutoCAD project and a greeting command; it has no UI, lens engine, or tests. AutoCAD references and package versions are already centralized in `Directory.Build.props`. AutoCAD 2025 and 2026 remain the target hosts.

This change introduces both a reusable exploration boundary and a first host-specific feature. Mockups define a direction, not a pixel-perfect requirement or evidence that drawing effects work inside AutoCAD.

## Goals / Non-Goals

**Goals:**

- Make grouping, navigation, and presentation testable without loading AutoCAD.
- Keep AutoCAD types and document ownership out of Core, UI, and lens implementations.
- Give temporary effects and asynchronous requests an explicit lifetime that cannot cross document boundaries.
- Validate the two host-sensitive parts early: modeless WPF interaction and temporary dimming.

**Non-goals:**

- A plugin discovery framework, arbitrary layout language, or cross-process service.
- A separate rendering engine, full replica of AutoCAD geometry, or replacement for its selection system.
- Persisting exploration history, toggle settings, or window position in this slice.

## Decisions

### 1. Four projects with inward dependencies

| Project | Responsibility | References |
| --- | --- | --- |
| `CadLens.Core` | Lens contracts, generic presentation records, navigation/session state, opaque object references, host-action contracts | No AutoCAD or WPF |
| `CadLens.Lenses` | Concrete lens implementations and their data-source contracts; initially Layers filtering, grouping, labels, and preview fields | Core |
| `CadLens.UI` | WPF views and view models for generic groups, details, breadcrumbs, filters, and actions | Core |
| `CadLens.AutoCAD` | Command/composition root, Layers snapshot adapter, host actions, graphics effects, document lifetime, modeless window hosting | Core, Lenses, UI, AutoCAD API |

Use `net8.0` for Core and Lenses, overriding the root default; UI and AutoCAD retain `net8.0-windows`. Only UI and the host composition project need `UseWPF`; only AutoCAD sets `UseAutocad`. Keep each project's root namespace throughout its folders. New folder settings disable Rider Namespace Provider alongside the first C# file.

Core describes a small fixed vocabulary: a lens identity, groups, metrics, detail fields, boolean filter descriptors, available actions, and navigation levels. A group carries an opaque identity and object references, not a layer object. A `HostObjectId` wraps the native identifier as a value; shared projects do not reference the host API. Open `DBObject` instances, WPF brushes, and view instances do not cross this boundary. UI maps generic icon roles and emphasis roles to its own resources. The Layers implementation in Lenses supplies the words and meaning of its filters and details; UI contains no tests for a lens named Layers or an AutoCAD primitive class.

The host registers one Layers provider through the Core lens interface using `Microsoft.Extensions.DependencyInjection`. Keep all registrations and service resolution in the AutoCAD composition boundary; Core, Lenses, UI views, and view models use constructor-injected interfaces without `IServiceProvider` or container dependencies. No assembly scanning or Generic Host is needed. Host and feature services expose interfaces at their boundaries. Expected unavailable states use a small typed result contract instead of exceptions for normal navigation.

Build one root service provider for the plugin lifetime with `ValidateScopes` and `ValidateOnBuild` enabled. The AutoCAD-created command entry point delegates to a narrowly exposed composition-root operation; it does not attempt constructor injection into an instance created by AutoCAD or expose a global service locator to application code. Keep the DI package reference and an exact .NET 8-compatible version in `Directory.Build.props`, conditional on the host project; verify the chosen version and transitive assemblies during restore and host loading.

| Lifetime | Ownership |
| --- | --- |
| Plugin root | Composition/session owner; no captured active document, window, or scoped feature service |
| Explorer scope | Session controller, one queued host task service, navigation state, Layers provider, snapshot adapter, visualization adapter, window, and root view model |
| Short-lived operation/context | Read transactions, locks, native object IDs, and snapshots of the current space; never singleton document state |

The composition owner creates one explicit scope when opening the explorer, resolves the session graph on the AutoCAD UI thread, and retains the scope until the window closes. Repeated commands reuse it. A document/space switch keeps the window's session scope but clears old presentation and graphics. The view model ignores results from a refresh started before that reset. Scoped adapters must not capture the opening document permanently. Scope lifetime and document lifetime are deliberately different; do not assume nested DI scopes provide inherited state.

Closing or failed initialization first stops new requests and performs host-sensitive cleanup on the proper AutoCAD thread/context, then disposes the scope exactly once. Coordinate in-flight callbacks before releasing their dependencies, without blocking the UI thread on an asynchronous task. Do not rely on DI disposal to provide the AutoCAD execution context. Container-owned dependencies are disposed by their scope rather than individually by consumers; AutoCAD-owned documents and databases are never owned by the container. Plugin termination closes the session before disposing the root provider. Test graph validation, fresh scoped instances after reopening, stale-context rejection, and disposal on normal and failed opens.

Alternative: manual composition. A DI container is selected to make the growing service graph and session lifetime explicit and verifiable. A full Generic Host would add a second application lifecycle without a current requirement for hosted services.

Keep concrete lenses together in `CadLens.Lenses`; do not create one assembly per lens. Layers is the first implementation, not a permanent architectural boundary. It can later evolve into an object overview with alternative grouping criteria without renaming Core or UI or changing their dependency direction. Keep layer-specific records and filtering close to that implementation; extract shared grouping logic only when a second real use case needs it. Future grouping modes are not part of this change.

Alternatives: put concrete lens logic in Core, make UI reference Lenses, or create a dedicated Layers assembly. Rejected because Core and UI should remain independent of concrete lenses, while a per-lens assembly would prematurely fix the shape of this experimental feature.

### 2. Read snapshots in the host, calculate in ordinary .NET

The Layers snapshot source contract is defined alongside its implementation in Lenses, with the data-source adapter implemented in AutoCAD. Enumerate only entities directly owned by the resolved active-space block table record, using read-only transactions. Use Database.CurrentSpaceId directly for the entity container; do not infer scope from CVPORT or the screen rectangle. Read global and active-viewport frozen state separately.

Snapshots contain the space label, layer identities/names/status, and entity IDs with assigned layer and primitive-type key. They do not contain document tokens, viewport keys, generations, or revision counters. Read actual runtime type identifiers into plain strings; map common types to friendly labels and preserve distinct labels for unknown/custom types. Count block references once, including external-reference insertions, without traversing definitions for inventory. Non-erased direct entities are counted even if their bounds cannot be obtained. Avoid conflating layer inclusion with whether a particular entity can currently draw.

Core uses `HostObjectId`, a one-field wrapper around the native identifier. AutoCAD stores its actual `ObjectId`, without converting it to a string handle and back. Never retain open database objects after a transaction. Future hosts can use their native identifiers when they are implemented. Do not build that integration in advance.

The Layers implementation filters snapshots and builds groups. Sort layer names predictably with ordinal case-insensitive ordering and identity as a tie-breaker; use a deterministic object-key order for Previous/Next. Navigation does not rescan the DWG. Type counts partition the included layer set exactly. Off and frozen filters combine conjunctively when both statuses apply; locked status does not exclude objects.

Alternative: ask AutoCAD selection APIs for objects visible on screen. Rejected because the agreed scope includes off-screen objects and optionally hidden layers.

### 3. One queued AutoCAD task service

`IHostTaskService` has one implementation, `AutoCadTaskService`. It owns the FIFO queue and calls `ExecuteInApplicationContext` directly on the AutoCAD dispatcher. It waits while an interactive command is busy, checks that the original document is still active, and locks the document for the synchronous action. No second execution service or context model is involved.

The current operations read inventory and manage temporary graphics; they do not prompt for input or need a command-context transition. Managed exceptions are caught inside the native callback. Cancellation wakes the caller and prevents queued work from starting; it does not interrupt a running native operation. Closing rejects new requests, skips pending actions, and drains the running callback before disposal.

Use `ObjectId.GetObject<T>()` for typed reads, with an optional `true` for writing, and `GetObjects<T>()` for symbol tables or block contents. The caller owns a regular StartTransaction() transaction and materializes results before disposing it. ObjectId.GetObject() does not work with OpenCloseTransaction. These extensions skip erased/invalid IDs; they do not create hidden transactions or retain open objects.

### 4. One session controller and explicit invalidation

CADLENS opens or activates the explorer without printing a greeting or requesting command-line input. One modeless window follows the active drawing. Repeated `CADLENS` brings it forward without recreating state in the same context. A fresh open creates a fresh session with both inclusion toggles disabled. A context switch clears selection and graphics and discards late UI results; the current preview refreshes the new space on request; session toggles remain until the window closes.

Subscribe to document activation/destruction, relevant layout/viewport transitions, and database changes while the session is open. Event handlers mark data dirty; collect again at a safe command boundary rather than reading inside database notifications. Reconcile context before every host action as protection against a missed notification. Coalesce edit bursts, including undo/redo, into one refresh. Preserve a selection only while its identities still exist and pass filters; otherwise return to the nearest valid ancestor. Revalidate targets before Focus and emphasis.

Close, document destruction, initialization failure, and plugin termination share idempotent cleanup: invalidate work, detach session events, remove owned graphics effects, refresh surviving affected views, and release managed/native resources. A window with no active drawing shows an unavailable state. Do not restore camera positions on close: explicit Focus navigation belongs to the user.

### 5. Modeless WPF shell with locally scoped resources

Use AutoCAD's `Application.ShowModelessWindow` host integration and owned-window behavior, following the nearby `AutocadWindowService` example. Avoid calling `Application.Run`, creating a second WPF Application, or replacing AutoCAD's application resources. Use a normal WPF window with custom chrome and WPF UI controls/resources scoped to that window. Do not rely on global topmost state or OS backdrop effects for correctness.

Pin `WPF-UI` 4.3.0 initially; its published package includes .NET 8 support. Keep its conditional reference/version in `Directory.Build.props`. Host restore/build and modeless loading still verify actual compatibility; package metadata alone is insufficient. Use CommunityToolkit.Mvvm 8.4.0 for observable view models and asynchronous commands, as requested during implementation review. Keep the package reference centralized and conditional on the UI project; no MVVM dependency belongs in Core or Lenses.

Start near the drawing area's left edge, using device-independent coordinates. Use a compact preferred width around 340 DIPs, constrained to available working area, with a scrollable/virtualized body and reachable header/footer. Handle DPI changes and resizing without clipping controls. Long layer names ellipsize with a full-name tooltip; breadcrumbs can collapse older ancestors while Back remains available.

The fixed panel shell renders four states: group list, group details/type list, type objects, and individual object details. A navigation stack preserves parent state. Object previews are detail fields within this shell for the first slice; the illustrated on-drawing hover tooltip is not an additional required interaction. Use the tactical mockup's density with softer corners, a high-contrast accent, subdued transitions, keyboard focus indication, and accessible names for icon controls. Keep layer color swatches distinct from UI emphasis colors. Do not bind CAD shortcuts globally while typing in controls.

Alternative: dockable PaletteSet or browser overlay. Rejected for this slice because the agreed interaction is a compact floating panel and WPF UI is selected. The UI assembly remains usable with fake providers outside AutoCAD for layout checks.

### 6. Temporary graphics owned by a visualization adapter

Use a scoped `DrawableOverrule` strategy for display-time emphasis/dimming, with ordinary object highlighting where appropriate for the current object. Represent the desired effect as immutable sets of opaque object references and emphasis roles. The host resolves them once into lookup sets for the current database/context. Draw callbacks do no inventory collection, asynchronous work, or persistent database writes.

Dim by altering rendering traits toward the drawing background and emphasize the target with a readable accent. Do not use layer off/frozen changes, isolation commands, entity color writes, or global fade variables. Such changes either affect stored data or interfere with unrelated user/plugin state. Respect actual hidden status; inclusion controls never force hidden objects to render.

Filter effects by owned database, active-space inventory, and session. Apply the same target and dimming sets in every viewport that displays those objects, respecting native visibility in each viewport; do not filter rendering to the active viewport. Remove only this plugin's overrule; never globally disable other plugins' overrules during cleanup. Keep graphics invalidation bounded to state changes. A render-only strategy may need special handling for text and block occurrences: count an insertion once, but distinguish its rendered occurrence from other instances of the same definition. Do not recolor shared block definitions. There is one exploration state, with no saved selection or navigation state per viewport. Document and space changes still clear the old effects. Focus affects only the active view.

Before integrating the full explorer, build and run a narrow host verification slice for this adapter and the window. Test explicit/ByLayer/ByBlock colors, MText, hatches, repeated block definitions with different insertion layers, viewport freezing, and multiple layout viewports. If rendering traits are insufficient, investigate transient render-only copies inside the same adapter, with deterministic disposal and no resident-object edits. This is a bounded implementation investigation, not permission to weaken the spec to highlighting alone. Failure to meet dimming/cleanup requirements blocks completion and requires revisiting the design.

Alternative: a translucent rectangle over the viewport. Rejected because it obscures input and cannot faithfully emphasize arbitrary CAD objects above the veil. A traits-only implementation without block/text verification is also insufficient.

### 7. Focus uses the active view's coordinate system

Resolve extents from current live objects, ignoring unavailable extents while reporting when no target has usable bounds. Convert world-space bounding-box corners to the active view's display coordinate system before fitting width/height, respecting view twist, aspect ratio, and a named padding constant. Do not change the active UCS or silently unlock a locked layout viewport. Report a locked viewport or unsupported view as unavailable. Focus and result filtering remain independent: valid hidden-object bounds can be focused without revealing geometry.

### 8. Verification follows the boundaries

Add meaningful Core/Lenses tests for filter combinations, viewport-freeze context, exact totals, blocks counted once, stable ordering, navigation restoration, invalidated requests, and stale/deleted targets. Test UI states with a fake lens containing long names, empty results, many groups, unavailable actions, and hidden objects. Use these fixtures to verify Core and UI do not require layer-specific types.

Build the solution with zero warnings and check Rider inspections separately. Package the plugin's managed dependencies beside its DLL without copying AutoCAD host assemblies. Verify clean loading, repeated commands, keyboard return to drawing, DPI/layout, Focus, document/space switching, editing/undo, and cleanup in the host. Compare relevant geometry/layer properties and `DBMOD` before/after read-only exploration; test Focus separately because view navigation is intentional. Record host/version and unsupported cases explicitly. A build or mocked test cannot certify graphics behavior.

## Risks / Trade-offs

- Graphics overrule limitations for text, blocks, custom/proxy entities, and viewport caching -> early host verification; instance-aware rendering where needed; no silent success for unmet dimming requirements.
- AutoCAD/WPF focus and DPI behavior -> use host window APIs and test dragging, typing, normal commands, and multiple monitors in AutoCAD.
- Large DWGs can stall snapshot reads or redraw -> read minimal metadata once per revision, virtualize lists, coalesce work, measure actual host latency before adding complexity.
- Document changes during queued actions -> a document reference check inside the task service, UI reset, cancellation, and idempotent cleanup.
- Locked viewports or invalid extents -> explicit unavailable Focus result; never modify drawing settings to force navigation.
- Package or host integration differences -> pin dependencies, preserve the existing runtime baseline, and distinguish AutoCAD/Civil 3D host results from build evidence.

## Migration Plan

There is no persisted data migration. Add project/dependency boundaries first, verify modeless hosting and graphics in a narrow slice, then integrate snapshot/grouping and UI navigation. Run automated and host checks before treating the feature as complete. Keep the main specs unchanged until the approved change is implemented and archived.

For rollback, close the explorer and unload through a supported host lifecycle or restart AutoCAD, then deploy the prior plugin build and its dependencies. No DWG repair should be necessary because this design does not write drawing data. Tasks will describe the implementation sequence next.

## References

- [Autodesk: graphics overrule color changes and limitations](https://blog.autodesk.io/displaying-entities-in-different-colors-using-drawableoverrule/).
- [Autodesk: .NET 8 plugin and command-context execution](https://blog.autodesk.io/creating-a-sample-autocad-plugin-with-net-80-and-ccli-for-autocad-2025/).
- [WPF UI 4.3.0 package and framework metadata](https://www.nuget.org/packages/WPF-UI/4.3.0).
- [Microsoft: dependency injection lifetime, scope validation, and disposal guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines).
- Local patterns inspected: `C:/dev/tools.fx/src/Autocad/PikTools.Autocad/Services/AutocadWindowService.cs`, `AutocadContextTaskService.cs`, and `../Extensions/WindowExtensions.cs`. These inform host adapters; they are not new project dependencies.