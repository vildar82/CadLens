namespace Common;

/// <summary>Identity of an object owned by a drawing host.</summary>
public interface IHostId
{
    /// <summary>Text used to identify the object in the explorer.</summary>
    string DisplayId { get; }
}
