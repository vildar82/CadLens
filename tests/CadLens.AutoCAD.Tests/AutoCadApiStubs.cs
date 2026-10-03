using System.Collections;
using System.Diagnostics.CodeAnalysis;

// Test doubles for native API boundaries; these do not certify behavior inside AutoCAD.
namespace Autodesk.AutoCAD.ApplicationServices
{
    internal sealed class Document
    {
        internal Editor Editor { get; } = new();
        internal DatabaseServices.Database Database { get; } = new();
        internal bool IsLocked { get; private set; }
        internal DrawingWindow Window { get; } = new();
        internal NativeDrawing Drawing { get; } = new();
        internal object GetAcadDocument() => Drawing;

        internal IDisposable LockDocument()
        {
            IsLocked = true;
            return new DocumentLock(this);
        }

        private sealed class DocumentLock(Document document) : IDisposable
        {
            public void Dispose() => document.IsLocked = false;
        }
    }

    internal sealed class Editor
    {
        internal bool IsQuiescent { get; set; } = true;
        internal DatabaseServices.ObjectId CurrentViewportObjectId { get; set; }
        internal DatabaseServices.ObjectId[] Selection { get; set; } = [];
        internal Func<EditorInput.PromptSelectionOptions, EditorInput.PromptSelectionResult>? Pick { get; set; }
        internal int PromptCount { get; private set; }
        internal EditorInput.PromptSelectionResult SelectImplied() => new(Selection);

        internal EditorInput.PromptSelectionResult GetSelection(EditorInput.PromptSelectionOptions options)
        {
            PromptCount++;
            return Pick!(options);
        }
    }

    internal sealed class DocumentCollection
    {
        private readonly Queue<Action> _callbacks = new();
        internal Document? MdiActiveDocument { get; set; } = new();
        internal DrawingWindow MainWindow { get; } = new();
        internal bool IsApplicationContext { get; private set; }
        internal int PendingCount => _callbacks.Count;
        internal Exception? SchedulingError { get; set; }
        internal int ActiveCommand { get; set; }

        internal void ExecuteInApplicationContext(Action<object> callback, object state)
        {
            if (ActiveCommand != 0)
                throw new InvalidOperationException("The queue must wait until the active command finishes.");

            if (SchedulingError is not null)
                throw SchedulingError;

            _callbacks.Enqueue(() => callback(state));
        }

        internal void ExecuteNext()
        {
            IsApplicationContext = true;

            try
            {
                _callbacks.Dequeue()();
            }
            finally
            {
                IsApplicationContext = false;
            }
        }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices.Core
{
    internal static class Application
    {
        internal static DocumentCollection DocumentManager { get; set; } = new();
        internal static DrawingWindow MainWindow => DocumentManager.MainWindow;
        internal static event EventHandler? Idle;
        internal static void RaiseIdle() => Idle?.Invoke(null, EventArgs.Empty);
        internal static object GetSystemVariable(string name) => name == "CMDACTIVE"
            ? DocumentManager.ActiveCommand
            : throw new ArgumentOutOfRangeException(nameof(name));
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    internal enum OpenMode
    {
        ForRead,
        ForWrite
    }

    /// <summary>Test double for the native ObjectId type.</summary>
    /// <param name="Value">Test identifier.</param>
    /// <param name="Database">Owning test database.</param>
    public readonly record struct ObjectId(int Value, Database Database)
    {
        internal bool IsValid => Database is not null && Database.Objects.ContainsKey(Value);
        internal bool IsErased => Database.Objects[Value].IsErased;
        internal int Handle => Value;

        internal DBObject GetObject(OpenMode mode, bool openErased, bool forceOpenOnLockedLayer) =>
            Database.TransactionManager.TopTransaction!.GetObject(this, mode, openErased, forceOpenOnLockedLayer);
    }

    // ReSharper disable once InconsistentNaming -- Matches the native AutoCAD API type.
    /// <summary>Test double for the native DBObject type.</summary>
    public class DBObject : Runtime.RXObject
    {
        internal bool IsErased { get; init; }
        internal ObjectId ObjectId { get; set; }
        internal Database Database => ObjectId.Database;
    }

    /// <summary>Test double for the native Database type.</summary>
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    public sealed class Database
    {
        internal Dictionary<int, DBObject> Objects { get; } = [];
        internal ObjectId CurrentSpaceId { get; set; }
        internal ObjectId LayerTableId { get; set; }
        internal bool TileMode => true;
        internal int Luprec => 4;
        internal int Auprec => 2;
        internal TransactionManager TransactionManager { get; } = new();

        internal ObjectId Add(DBObject value)
        {
            var id = Objects.Count + 1;
            Objects.Add(id, value);
            value.ObjectId = new ObjectId(id, this);
            return value.ObjectId;
        }
    }

    internal sealed class TransactionManager
    {
        internal Transaction TopTransaction { get; } = new();
        internal Transaction StartTransaction() => TopTransaction;
    }

    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    internal sealed class Transaction : IDisposable
    {
        internal void Commit() { }
        public void Dispose() { }
        internal List<(OpenMode Mode, bool OpenErased, bool ForceOpenOnLockedLayer)> OpenRequests { get; } = [];

        internal DBObject GetObject(
            ObjectId id,
            OpenMode mode,
            bool openErased,
            bool forceOpenOnLockedLayer)
        {
            OpenRequests.Add((mode, openErased, forceOpenOnLockedLayer));
            return id.Database.Objects[id.Value];
        }
    }

    /// <summary>Test double for the native Entity type.</summary>
    public class Entity : DBObject
    {
        internal ObjectId OwnerId { get; init; }
        internal ObjectId LayerId { get; init; }
    }

    /// <summary>Test double for the native SymbolTable type.</summary>
    /// <param name="ids">Contained test identifiers.</param>
    public class SymbolTable(params ObjectId[] ids) : DBObject, IEnumerable
    {
        /// <inheritdoc />
        public IEnumerator GetEnumerator() => ids.GetEnumerator();
    }

    /// <summary>Test double for the native BlockTableRecord type.</summary>
    /// <param name="ids">Contained test identifiers.</param>
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    public sealed class BlockTableRecord(params ObjectId[] ids) : DBObject, IEnumerable
    {
        internal ObjectId LayoutId { get; init; }
        internal bool IsLayout => false;
        internal string Name => "Model";
        /// <inheritdoc />
        public IEnumerator GetEnumerator() => ids.GetEnumerator();
    }
}
