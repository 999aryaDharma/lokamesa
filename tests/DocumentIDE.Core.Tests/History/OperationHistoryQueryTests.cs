using DocumentIDE.Core.History;
using Xunit;

namespace DocumentIDE.Core.Tests.History;

/// <summary>
/// HIST-04 — pagination is bounded before any read. The Git history convention
/// (default 50, maximum 200) is reused so both read surfaces page identically.
/// </summary>
public sealed class OperationHistoryQueryTests
{
    [Fact]
    public void Defaults_MatchTheSourceControlConvention()
    {
        var query = OperationHistoryQuery.Default;

        Assert.Equal(0, query.Skip);
        Assert.Equal(50, query.Limit);
        Assert.Equal(50, OperationHistoryQuery.DefaultLimit);
        Assert.Equal(200, OperationHistoryQuery.MaxLimit);
    }

    [Theory]
    [InlineData(null, OperationHistoryQuery.DefaultLimit)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(199, 199)]
    [InlineData(200, 200)]
    [InlineData(201, OperationHistoryQuery.MaxLimit)]
    [InlineData(100_000, OperationHistoryQuery.MaxLimit)]
    public void Limit_IsClampedToTheBoundedWindow(int? limit, int expected)
    {
        Assert.True(OperationHistoryQuery.TryNormalize(skip: null, limit, out var query, out var errorCode, out var error));

        Assert.Null(errorCode);
        Assert.Null(error);
        Assert.Equal(expected, query.Limit);
        Assert.Equal(0, query.Skip);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    public void Skip_IsHonoured(int? skip, int expected)
    {
        Assert.True(OperationHistoryQuery.TryNormalize(skip, limit: null, out var query, out _, out _));
        Assert.Equal(expected, query.Skip);
    }

    [Fact]
    public void NegativeSkip_IsRejectedBeforeAnyRead()
    {
        Assert.False(OperationHistoryQuery.TryNormalize(-1, limit: null, out _, out var errorCode, out var error));
        Assert.Equal("InvalidSkip", errorCode);
        Assert.Contains("must not be negative", error, StringComparison.Ordinal);
    }
}
