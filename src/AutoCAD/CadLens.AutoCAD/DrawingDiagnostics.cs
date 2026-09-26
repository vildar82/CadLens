using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal static class DrawingDiagnostics
{
    internal static DrawingSnapshot Capture(Document document)
    {
        var database = document.Database;
        var types = new Dictionary<string, int>(StringComparer.Ordinal);
        var activeSpaceTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<DiagnosticEntitySnapshot>();
        var hatches = new List<HatchSnapshot>();
        var selected = new List<DiagnosticEntitySnapshot>();
        var layerIds = new HashSet<ObjectId>();
        var layers = new List<DiagnosticLayerSnapshot>();
        var errors = new List<string>();
        var implied = document.Editor.SelectImplied();

        using (database.TransactionManager.StartTransaction())
        {
            var space = database.GetActiveSpace();
            var blockTable = database.BlockTableId.GetObject<BlockTable>()!;

            foreach (var owner in blockTable.GetObjects<BlockTableRecord>())
            {
                foreach (ObjectId id in owner)
                {
                    try
                    {
                        var entity = id.GetObject<Entity>();

                        if (entity is null)
                            continue;

                        var type = $"{id.ObjectClass.DxfName} ({entity.GetType().FullName})";
                        types[type] = types.GetValueOrDefault(type) + 1;
                        layerIds.Add(entity.LayerId);

                        if (owner.ObjectId == space.ObjectId)
                            activeSpaceTypes[type] = activeSpaceTypes.GetValueOrDefault(type) + 1;

                        if (types[type] <= 3 ||
                            owner.ObjectId == space.ObjectId && activeSpaceTypes[type] <= 3)
                            examples.Add(DescribeEntity(entity, owner.Name, errors));

                        if (entity is Hatch hatch)
                            hatches.Add(DescribeHatch(hatch, owner.Name, errors));
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"Object {id.Handle} in {owner.Name}: {exception.Message}");
                    }
                }
            }

            foreach (var id in layerIds)
            {
                try
                {
                    if (id.GetObject<LayerTableRecord>() is not { } layer)
                        continue;

                    layers.Add(new DiagnosticLayerSnapshot(
                        layer.Name,
                        layer.IsOff,
                        layer.IsFrozen,
                        layer.IsLocked,
                        DescribeColor(layer.Color)));
                }
                catch (Exception exception)
                {
                    errors.Add($"Layer {id.Handle}: {exception.Message}");
                }
            }

            if (implied.Status == PromptStatus.OK)
            {
                foreach (var id in implied.Value.GetObjectIds())
                {
                    try
                    {
                        if (id.GetObject<Entity>() is { } entity)
                        {
                            var owner = entity.OwnerId.GetObject<BlockTableRecord>()?.Name ?? "Unknown";
                            selected.Add(DescribeEntity(entity, owner, errors));
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"Selected object {id.Handle}: {exception.Message}");
                    }
                }
            }
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in new[] { "ACADVER", "CTAB", "TILEMODE", "CVPORT", "LWDISPLAY", "FILLMODE", "TRANSPARENCYDISPLAY", "DBMOD" })
        {
            try
            {
                settings[name] = Application.GetSystemVariable(name)?.ToString() ?? "null";
            }
            catch (Exception exception)
            {
                settings[name] = $"Unavailable: {exception.Message}";
            }
        }

        return new DrawingSnapshot(
            2,
            DateTimeOffset.UtcNow,
            Path.GetFileName(document.Name),
            types,
            activeSpaceTypes,
            examples,
            hatches,
            selected,
            layers,
            settings,
            errors);
    }

    internal static async Task<string> SaveAsync(DrawingSnapshot snapshot)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"cadlens-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, new JsonSerializerOptions { WriteIndented = true });

        return path;
    }

    private static HatchSnapshot DescribeHatch(Hatch hatch, string owner, List<string> errors)
    {
        var handle = hatch.Handle.ToString();
        var isGradient = Read(() => hatch.IsGradient, false, errors, handle, "IsGradient");

        return new HatchSnapshot(
            DescribeEntity(hatch, owner, errors),
            Read(() => hatch.PatternName, "Unavailable", errors, handle, "PatternName"),
            Read(() => hatch.PatternType.ToString(), "Unavailable", errors, handle, "PatternType"),
            isGradient ? Read(() => hatch.GradientName, "Unavailable", errors, handle, "GradientName") : null,
            Read(() => hatch.IsSolidFill, false, errors, handle, "IsSolidFill"),
            isGradient,
            Read(() => hatch.Associative, false, errors, handle, "Associative"),
            Read(() => hatch.HatchStyle.ToString(), "Unavailable", errors, handle, "HatchStyle"),
            isGradient ? null : Read<double?>(() => hatch.PatternScale, null, errors, handle, "PatternScale"),
            isGradient ? null : Read<double?>(() => hatch.PatternAngle, null, errors, handle, "PatternAngle"),
            Read(() => hatch.NumberOfLoops, -1, errors, handle, "NumberOfLoops"),
            Read(() => DescribeColor(hatch.BackgroundColor), new ColorSnapshot("Unavailable", null, null), errors, handle, "BackgroundColor"));
    }

    private static DiagnosticEntitySnapshot DescribeEntity(Entity entity, string owner, List<string> errors)
    {
        var handle = entity.Handle.ToString();

        return new DiagnosticEntitySnapshot(
            handle,
            owner,
            entity.ObjectId.ObjectClass.DxfName,
            entity.GetType().FullName ?? entity.GetType().Name,
            Read(() => entity.Layer, "Unavailable", errors, handle, "Layer"),
            entity is BlockReference block
                ? Read(() => block.BlockTableRecord.GetObject<BlockTableRecord>()?.Name, null, errors, handle, "ReferencedBlock")
                : null,
            Read(() => entity.Visible, false, errors, handle, "Visible"),
            Read(() => entity.LineWeight.ToString(), "Unavailable", errors, handle, "LineWeight"),
            Read(() => entity.Transparency.Alpha, byte.MinValue, errors, handle, "Transparency"),
            Read(() => DescribeColor(entity.Color), new ColorSnapshot("Unavailable", null, null), errors, handle, "Color"));
    }

    private static T Read<T>(Func<T> getter, T fallback, List<string> errors, string handle, string field)
    {
        try
        {
            return getter();
        }
        catch (Exception exception)
        {
            errors.Add($"Object {handle} {field}: {exception.Message}");
            return fallback;
        }
    }

    private static ColorSnapshot DescribeColor(Color color) => new(
        color.ColorMethod.ToString(),
        color.ColorMethod == ColorMethod.ByAci ? color.ColorIndex : null,
        color.ColorMethod == ColorMethod.ByColor ? $"{color.Red},{color.Green},{color.Blue}" : null);
}

internal sealed record DrawingSnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string Drawing,
    Dictionary<string, int> EntityTypes,
    Dictionary<string, int> ActiveSpaceEntityTypes,
    List<DiagnosticEntitySnapshot> EntityExamples,
    List<HatchSnapshot> Hatches,
    List<DiagnosticEntitySnapshot> SelectedEntities,
    List<DiagnosticLayerSnapshot> Layers,
    Dictionary<string, string> DisplaySettings,
    List<string> ReadErrors);

internal sealed record HatchSnapshot(
    DiagnosticEntitySnapshot Entity,
    string PatternName,
    string PatternType,
    string? GradientName,
    bool IsSolidFill,
    bool IsGradient,
    bool IsAssociative,
    string HatchStyle,
    double? PatternScale,
    double? PatternAngle,
    int LoopCount,
    ColorSnapshot BackgroundColor);

internal sealed record DiagnosticEntitySnapshot(
    string Handle,
    string Owner,
    string DxfName,
    string ManagedType,
    string Layer,
    string? ReferencedBlock,
    bool Visible,
    string LineWeight,
    byte TransparencyAlpha,
    ColorSnapshot Color);

internal sealed record ColorSnapshot(string Method, int? Index, string? Rgb);

internal sealed record DiagnosticLayerSnapshot(
    string Name,
    bool IsOff,
    bool IsFrozen,
    bool IsLocked,
    ColorSnapshot Color);
