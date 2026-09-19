namespace CadLens.Core;

/// <summary>Composes host operations while preserving an unavailable result and its explanation.</summary>
public static class HostResultExtensions
{
    /// <summary>Continues with a successful value; skips the continuation when the host is unavailable.</summary>
    /// <typeparam name="T">Source value type.</typeparam>
    /// <typeparam name="TResult">Continuation value type.</typeparam>
    /// <param name="result">Completed host operation.</param>
    /// <param name="next">Operation to run for a successful value.</param>
    public static HostResult<TResult> Bind<T, TResult>(
        this HostResult<T> result,
        Func<T, HostResult<TResult>> next) => result switch
    {
        HostResult<T>.Success success => next(success.Value),
        HostResult<T>.Unavailable unavailable => new HostResult<TResult>.Unavailable(unavailable.Reason),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };

    /// <summary>Converts either outcome into a value for the caller.</summary>
    /// <typeparam name="T">Source value type.</typeparam>
    /// <typeparam name="TResult">Returned value type.</typeparam>
    /// <param name="result">Completed host operation.</param>
    /// <param name="onSuccess">Converts the successful value.</param>
    /// <param name="onUnavailable">Converts the unavailable explanation.</param>
    public static TResult Match<T, TResult>(
        this HostResult<T> result,
        Func<T, TResult> onSuccess,
        Func<string, TResult> onUnavailable) => result switch
    {
        HostResult<T>.Success success => onSuccess(success.Value),
        HostResult<T>.Unavailable unavailable => onUnavailable(unavailable.Reason),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}