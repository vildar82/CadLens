# Tasks

## 1. Project and dependencies

- [x] 1.1 Create `src/AutoCAD/CadLens.AutoCAD/CadLens.AutoCAD.csproj` for `net8.0-windows`; keep package versions and references in the root `Directory.Build.props`, enable `AutoCAD.NET` 25.0.1 through `UseAutocad=true` in the `.csproj`, and add `JetBrains.Annotations` to all projects; verify restore and the absence of AutoCAD package DLLs in the output directory.
- [x] 1.2 Add the project to `CadLens.slnx`; verify that `dotnet sln CadLens.slnx list` includes `CadLens.AutoCAD.csproj`.

## 2. CADLENS command

- [x] 2.1 Register a document command named `CADLENS` that writes a greeting containing `CAD Lens` through the editor without accessing the DWG database; mark the AutoCAD-invoked class and method with `UsedImplicitly`; verify compilation and the absence of drawing modifications.
- [x] 2.2 Document the build, manual `NETLOAD`, and `CADLENS` invocation in `README.md`; verify that the instructions and DLL path match the build output.

## 3. Civil 3D verification and compatibility limits

- [x] 3.1 Load the DLL with `NETLOAD` in Civil 3D 2026 based on AutoCAD 2026, run `CADLENS` with a DWG open, and record the host version, greeting, completion without prompts, and unchanged drawing state.
- [x] 3.2 State explicitly in `README.md` that separate AutoCAD 2025/2026 installations have not been tested; installing and testing them is not required to complete this first slice.
- [x] 3.3 Run `openspec validate autocad-bootstrap --strict` and compare the in-host results with `specs/autocad-command/spec.md`; ensure each requirement has either a verified result or a clearly recorded gap.