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
    /// <summary>Test double for native drawable graphics.</summary>
    public class Drawable;

    /// <summary>Test double for native drawable traits.</summary>
    public sealed class DrawableTraits;

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
        internal void SetCustomFilter() { }

        /// <summary>Tests whether the native object is affected by this overrule.</summary>
        public virtual bool IsApplicable(Runtime.RXObject subject) => false;

        /// <summary>Returns the base graphics attributes.</summary>
        public virtual int SetAttributes(Drawable drawable, DrawableTraits traits) => 0;
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
