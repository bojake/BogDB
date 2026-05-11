using BogDb.Core.Extraction;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphExternalIdTests
{
    [Fact]
    public void FormatNodeId_SameInput_IsStable()
    {
        var first = GraphExternalIdFormatter.FormatNodeId("Person", 42L);
        var second = GraphExternalIdFormatter.FormatNodeId("Person", 42L);

        Assert.Equal(first, second);
        Assert.Equal("node:Person:42", first);
    }

    [Fact]
    public void FormatNodeId_TableQualifiedIds_DoNotCollide()
    {
        var personId = GraphExternalIdFormatter.FormatNodeId("Person", "abc");
        var cityId = GraphExternalIdFormatter.FormatNodeId("City", "abc");

        Assert.NotEqual(personId, cityId);
    }

    [Fact]
    public void FormatNodeId_EscapesReservedCharacters_AndRoundTrips()
    {
        var externalId = GraphExternalIdFormatter.FormatNodeId("People:Archive", "a/b:c?d");

        Assert.True(GraphExternalIdFormatter.TryParseNodeId(externalId, out var tableName, out var nodeId));
        Assert.Equal("People:Archive", tableName);
        Assert.Equal("a/b:c?d", nodeId);
    }

    [Fact]
    public void TryParseNodeId_InvalidPayload_ReturnsFalse()
    {
        Assert.False(GraphExternalIdFormatter.TryParseNodeId("bad-prefix:Person:42", out _, out _));
        Assert.False(GraphExternalIdFormatter.TryParseNodeId("node:Person", out _, out _));
        Assert.False(GraphExternalIdFormatter.TryParseNodeId("node::42", out _, out _));
    }

    [Fact]
    public void FormatNodeId_NullOrEmptyInputs_FailPredictably()
    {
        Assert.Throws<ArgumentException>(() => GraphExternalIdFormatter.FormatNodeId("", 1L));
        Assert.Throws<ArgumentNullException>(() => GraphExternalIdFormatter.FormatNodeId("Person", null!));
    }
}
