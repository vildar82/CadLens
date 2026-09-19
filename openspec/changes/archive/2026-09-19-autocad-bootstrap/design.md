# Design

## Context

The required behavior is described in `specs/autocad-command/spec.md`; the motivation and scope of the first slice are in `proposal.md`. Civil 3D 2026 based on AutoCAD 2026 is available on this machine for manual verification. Separate AutoCAD 2025 and 2026 installations were not found.

AutoCAD 2025 and 2026 originally ran on .NET 8, but the 2025.1.4 and 2026.1.2 updates move the hosts to .NET 10. According to the [Autodesk compatibility table](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-D54B0935-1638-4F97-8B37-1EC3635A1E71.htm) and [developer announcement](https://blog.autodesk.io/autodesk-desktop-products-2025-2026-net-10-updates/), .NET 8 plugins generally continue to work after the update, but this requires verification in the target host.

## Goals / Non-Goals

**Goals:**

- Create one small library project that can be built and manually loaded in AutoCAD 2025/2026.
- Make the command result visible on the command line without changing the drawing database.
- Keep a successful build distinct from verification of loading and execution inside AutoCAD.

**Non-Goals:**

- Add infrastructure for DI, windows, ribbon integration, or the lifecycle of future lenses before there is behavior that needs it.
- Configure permanent plugin loading or installation on other machines.

## Decisions

### One project with a minimal entry point

Create an SDK-style library at `src/AutoCAD/CadLens.AutoCAD/CadLens.AutoCAD.csproj`, target `net8.0-windows`, and add the project to `CadLens.slnx`. Keep shared build properties and package versions in the root `Directory.Build.props`. Register the command with `CommandMethod("CADLENS")` on its command class. Annotate the class and method with `UsedImplicitly`: AutoCAD invokes them through command registration, so Rider does not see ordinary code references. The normal document context is sufficient because the command runs with a DWG open and does not need background access to other documents. The standard AutoCAD API was chosen over a custom registration mechanism or RxBim commands so the first slice does not depend on infrastructure it does not yet need. [Autodesk command documentation](https://help.autodesk.com/cloudhelp/2015/PLK/AutoCAD-NET/files/GUID-F77E8FE0-8034-4704-93BD-F717608F8223.htm).

### Write through the editor without accessing the DWG database

The command obtains the active document's editor and calls `Editor.WriteMessage` with a greeting containing `CAD Lens`. It does not start transactions, read database objects, or call AutoCAD commands that modify the drawing. `NoUndoMarker` is suitable for a command that does not change the database. A separate output service would have added a layer without another consumer. [Editor.WriteMessage reference](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_EditorInput_Editor_WriteMessage_string_params_object__.html).

### AutoCAD NuGet package and version compatibility

Add references to [Autodesk `AutoCAD.NET` 25.0.1](https://www.nuget.org/packages/AutoCAD.NET/25.0.1) and `JetBrains.Annotations` 2025.2.4 in the root `Directory.Build.props`. All projects receive `JetBrains.Annotations`; only projects with `UseAutocad=true` receive `AutoCAD.NET`. Set that property in the `.csproj` so it is available when the conditional `ItemGroup` in the root props file is evaluated. This keeps all first-slice dependency versions in one file. The AutoCAD package provides the AutoCAD 2025 API for .NET 8 and resolves its dependencies through NuGet. Do not specify paths to an installed SDK or AutoCAD, or add direct DLL references. The package assemblies are needed for compilation; AutoCAD provides the API at runtime. Exclude package runtime assets from publication and verify that AutoCAD DLLs are not copied to the plugin output. According to the [Autodesk compatibility matrix](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm), the AutoCAD 2025 API is supported in AutoCAD 2026. Choosing `AutoCAD.NET` 25.1.x instead would have tied this greeting-only command to a newer API without a need for it.

### Manual loading and verification

The first loading path is `NETLOAD` of the built DLL from a trusted directory, followed by `CADLENS` with a DWG open. Verification in the available Civil 3D 2026 is sufficient for the first slice: the greeting appears, the command completes without further prompts, and the DWG state remains unchanged. Compatibility with separate AutoCAD 2025/2026 installations and hosts updated to .NET 10 remains unverified until those hosts are available. A build and OpenSpec validation check only the static parts. [Autodesk loading guide](https://help.autodesk.com/cloudhelp/2026/KOR/OARX-DevGuide-Managed/files/GUID-577EDC34-0A10-4D63-BED4-ECA4570355A3.htm).

## Risks / Trade-offs

- [`AutoCAD.NET` cannot be restored or a separate AutoCAD host is unavailable] → restore the pinned package version from NuGet; treat a restore failure as a build blocker and an unavailable host as an unverified compatibility case, not a passing test.
- [A host update to .NET 10 may reveal a DLL incompatibility] → check loading and command execution in the updated AutoCAD; if a failure is confirmed, add a separate `net10.0-windows` build without changing the command contract.
- [AutoCAD rejects `NETLOAD` because of `SECURELOAD`/`TRUSTEDPATHS`] → load the DLL from a trusted directory without disabling AutoCAD security.

## Migration Plan

There is no permanent installation or DWG migration. For a trial run, build the DLL, load it through `NETLOAD`, and run the command. To roll back, remove the DLL from the trial directory and restart AutoCAD: a loaded .NET assembly cannot be unloaded from the current process with a normal command.