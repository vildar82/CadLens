using System.Diagnostics.CodeAnalysis;

// Native API doubles expose the production graphics lifecycle to managed tests.
namespace Autodesk.AutoCAD.Runtime
{
    /// <summary>Test double for the native runtime base object.</summary>
    public class RXObject
    {
        internal static Type GetClass(Type type) => type;
    }
}

namespace Autodesk.AutoCAD.GraphicsInterface
{
    internal enum TransientDrawingMode
    {
        Main
    }

    internal sealed class TransientManager
    {
        internal static TransientManager CurrentTransientManager { get; set; } = new();
        internal HashSet<DatabaseServices.Entity> Registered { get; } = [];
        internal Func<DatabaseServices.Entity, int, bool>? OnAdd { get; set; }
        internal Func<DatabaseServices.Entity, bool>? OnErase { get; set; }
        internal int AddCount { get; private set; }

        internal bool AddTransient(
            DatabaseServices.Entity drawable,
            TransientDrawingMode mode,
            int subDrawingMode,
            Geometry.IntegerCollection viewportNumbers)
        {
            if (mode != TransientDrawingMode.Main || subDrawingMode != 128 || viewportNumbers is null)
                throw new InvalidOperationException("Unexpected transient registration.");

            Registered.Add(drawable);
            AddCount++;

            if (OnAdd?.Invoke(drawable, AddCount) != false)
                return true;

            Registered.Remove(drawable);
            return false;
        }

        internal bool EraseTransient(DatabaseServices.Entity drawable, Geometry.IntegerCollection viewportNumbers)
        {
            if (viewportNumbers is null)
                throw new InvalidOperationException("Unexpected transient removal.");

            return OnErase?.Invoke(drawable) != false && Registered.Remove(drawable);
        }
    }

    /// <summary>Test double for native drawable graphics.</summary>
    public class Drawable : Runtime.RXObject;

    /// <summary>Test double for native drawable traits.</summary>
    public sealed class DrawableTraits
    {
        internal Colors.EntityColor? TrueColor { get; set; }
    }

    /// <summary>Test double for the native world drawing context.</summary>
    public sealed class WorldDraw;

    /// <summary>Test double for the native invisible graphics flag.</summary>
    public enum DrawableAttributes
    {
        /// <summary>The drawable is hidden.</summary>
        IsInvisible = 1
    }

    /// <summary>Test double for native overrule registration.</summary>
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    public class DrawableOverrule : Runtime.RXObject
    {
        internal static bool Overruling { get; set; }
        internal static HashSet<DrawableOverrule> Registered { get; } = [];
        internal bool CustomFilter { get; private set; }

        internal static void AddOverrule(Type type, DrawableOverrule rule, bool addAtLast)
        {
            if (type != typeof(DatabaseServices.Entity) || addAtLast)
                throw new InvalidOperationException("Unexpected native overrule registration.");

            Registered.Add(rule);
        }

        internal static void RemoveOverrule(Type type, DrawableOverrule rule)
        {
            if (type != typeof(DatabaseServices.Entity))
                throw new InvalidOperationException("Unexpected native overrule removal.");

            Registered.Remove(rule);
        }
        internal void SetCustomFilter() => CustomFilter = true;

        /// <summary>Tests whether the native object is affected by this overrule.</summary>
        public virtual bool IsApplicable(Runtime.RXObject subject) => false;

        /// <summary>Returns the base graphics attributes.</summary>
        public virtual int SetAttributes(Drawable drawable, DrawableTraits traits) => 0;

        /// <summary>Runs the native drawing, which may draw nested objects.</summary>
        public virtual bool WorldDraw(Drawable drawable, WorldDraw wd)
        {
            NestedDrawing?.Invoke();
            return true;
        }

        internal Action? NestedDrawing { get; set; }
    }
}

namespace Autodesk.AutoCAD.PlottingServices
{
    internal sealed class BeginPlotEventArgs;

    internal sealed class EndPlotEventArgs;

    internal sealed class PlotReactorManager
    {
        internal PlotReactorManager() => Latest = this;

        internal static PlotReactorManager? Latest { get; private set; }
        internal bool IsWatched => BeginPlot is not null || EndPlot is not null;

        internal event Action<object, BeginPlotEventArgs>? BeginPlot;
        internal event Action<object, EndPlotEventArgs>? EndPlot;

        internal void RaiseBeginPlot() => BeginPlot?.Invoke(this, new BeginPlotEventArgs());
        internal void RaiseEndPlot() => EndPlot?.Invoke(this, new EndPlotEventArgs());
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    internal sealed class DrawingWindow
    {
        internal bool FocusSuccess { get; set; } = true;
        internal Action? OnFocus { get; set; }
        internal int FocusCount { get; private set; }

        internal bool Focus()
        {
            FocusCount++;
            OnFocus?.Invoke();
            return FocusSuccess;
        }
    }

    /// <summary>Test double for the COM drawing regeneration boundary.</summary>
    public sealed class NativeDrawing
    {
        internal Action? OnRegen { get; set; }
        internal int RegenCount { get; private set; }

        /// <summary>Simulates regenerating all viewports.</summary>
        /// <param name="viewports">Native viewport regeneration mode.</param>
        public void Regen(int viewports)
        {
            if (viewports != 1)
                throw new InvalidOperationException("Isolation must regenerate all viewports.");

            RegenCount++;
            OnRegen?.Invoke();
        }
    }
}
