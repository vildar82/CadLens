using Xunit;

namespace CadLens.Core.Tests;

/// <summary>Checks that result composition preserves failures and selects only the matching outcome.</summary>
public sealed class HostResultExtensionsTests
{
    /// <summary>Unavailable host operations must not invoke subsequent work.</summary>
    [Fact]
    public void BindSkipsWorkWhenHostIsUnavailable()
    {
        HostResult<int> result = new HostResult<int>.Unavailable("Drawing closed.");
        var called = false;

        var next = result.Bind<int, string>(value =>
        {
            called = true;
            return new HostResult<string>.Success(value.ToString());
        });

        Assert.False(called);
        Assert.Equal("Drawing closed.", Assert.IsType<HostResult<string>.Unavailable>(next).Reason);
    }

    /// <summary>A continuation can reject a successful source, such as a snapshot from another drawing.</summary>
    [Fact]
    public void BindPreservesContinuationFailure()
    {
        HostResult<int> result = new HostResult<int>.Success(42);

        var next = result.Bind<int, string>(value => new HostResult<string>.Unavailable($"Stale revision {value}."));

        Assert.Equal("Stale revision 42.", Assert.IsType<HostResult<string>.Unavailable>(next).Reason);
    }

    /// <summary>Only the handler matching the result is executed.</summary>
    [Theory]
    [InlineData(true, "42")]
    [InlineData(false, "Drawing closed.")]
    public void MatchSelectsOneOutcome(bool successful, string expected)
    {
        HostResult<int> result = successful
            ? new HostResult<int>.Success(42)
            : new HostResult<int>.Unavailable("Drawing closed.");
        var calls = 0;

        var message = result.Match(
            value =>
            {
                calls++;
                return value.ToString();
            },
            reason =>
            {
                calls++;
                return reason;
            });

        Assert.Equal(expected, message);
        Assert.Equal(1, calls);
    }
}