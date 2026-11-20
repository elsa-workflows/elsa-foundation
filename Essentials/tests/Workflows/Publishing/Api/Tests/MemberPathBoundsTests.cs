using System.Text.Json;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The authored member path is read in one place, and its bounds are the compiled read selection's: a path the
/// authoring side accepts is one the runtime model accepts, and one it refuses the runtime model refuses too.
/// </summary>
public sealed class MemberPathBoundsTests
{
    public static TheoryData<int, int> Paths => new()
    {
        { 1, 0 }, { 1, 4096 }, { 64, 1 }, { 64, 64 }, { 65, 1 }, { 1, 4097 }, { 2, 2048 }, { 2, 2049 }
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public void The_authored_member_path_and_the_compiled_selection_agree_on_their_bounds(int count, int segmentLength)
    {
        string[] segments = [.. Enumerable.Repeat(new string('a', segmentLength), count)];

        var authored = ActivityDataMemberPath.TryRead(JsonSerializer.SerializeToElement(new { memberPath = segments }), out var read);
        var compiled = Record.Exception(() => new RuntimeReadSelection(1, segments, RuntimeReadSelectionMode.DeferredFit)) is null;

        Assert.Equal(compiled, authored);
        Assert.Equal(compiled, ActivityDataMemberPath.IsWithinBounds(segments));
        if (authored)
            Assert.Equal(segments, read.ToArray());
    }

    [Theory]
    [InlineData("""{"memberPath":[]}""")]
    [InlineData("""{"memberPath":"a"}""")]
    [InlineData("""{"memberPath":["a",null]}""")]
    [InlineData("""{"memberPath":["a",1]}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    public void A_malformed_member_path_is_not_read(string payload)
    {
        Assert.False(ActivityDataMemberPath.TryRead(JsonDocument.Parse(payload).RootElement, out _));
    }
}
