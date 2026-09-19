namespace Common;

/// <summary>A successful value or an expected unavailable state.</summary>
/// <typeparam name="T">Successful value type.</typeparam>
public abstract record HostResult<T>
{
    private HostResult() { }

    /// <summary>A completed operation.</summary>
    /// <param name="Value">Operation value.</param>
    public sealed record Success(T Value) : HostResult<T>;

    /// <summary>An operation that could not be performed in the requested context.</summary>
    /// <param name="Reason">User-facing explanation.</param>
    public sealed record Unavailable(string Reason) : HostResult<T>;
}