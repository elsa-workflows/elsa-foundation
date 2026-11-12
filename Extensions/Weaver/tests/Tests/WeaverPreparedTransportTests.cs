using System.Text.Json;
using Elsa.Weaver.Api;
using Elsa.Weaver.Api.Models;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

/// <summary>Generated API/SSE graphs preserve classed preparations and the omitted legacy optional-field shape.</summary>
public sealed class WeaverPreparedTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generated_HTTP_and_SSE_preparations_preserve_required_classes_and_the_review_digest(bool classed)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var original = await fixture.ProposeAsync();
        WeaverPrivilegeClass[] classes = classed
            ? [WeaverPrivilegeClass.Administrative, WeaverPrivilegeClass.Irrecoverable, WeaverPrivilegeClass.Activation]
            : [];
        var prepared = original.PreparedInvocation! with { RequiredPrivilegeClasses = classes };
        prepared = prepared with { ApprovalDigest = WeaverInvocationIntegrity.PreparedDigest(prepared) };
        if (classed) Assert.NotEqual(original.PreparedInvocation.ApprovalDigest, prepared.ApprovalDigest);
        else Assert.Equal(original.PreparedInvocation.ApprovalDigest, prepared.ApprovalDigest);
        var proposal = original with { PreparedInvocation = prepared };

        var httpJson = JsonSerializer.Serialize(WeaverApiResponse<WeaverActionProposal>.Success(proposal),
            WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal);
        using var http = JsonDocument.Parse(httpJson);
        AssertShape(http.RootElement.GetProperty("data").GetProperty("preparedInvocation"));
        var httpRead = JsonSerializer.Deserialize(httpJson, WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal)!;
        AssertRead(httpRead.Data!.PreparedInvocation!);

        var sseJson = JsonSerializer.Serialize(prepared, WeaverSseJsonContext.Default.WeaverPreparedToolInvocation);
        using var sse = JsonDocument.Parse(sseJson);
        AssertShape(sse.RootElement);
        AssertRead(JsonSerializer.Deserialize(sseJson, WeaverSseJsonContext.Default.WeaverPreparedToolInvocation)!);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);

        void AssertShape(JsonElement value)
        {
            Assert.Equal(prepared.ApprovalDigest, value.GetProperty("approvalDigest").GetString());
            if (classed)
                Assert.Equal(["administrative", "irrecoverable", "activation"],
                    value.GetProperty("requiredPrivilegeClasses").EnumerateArray().Select(x => x.GetString()));
            else Assert.False(value.TryGetProperty("requiredPrivilegeClasses", out _));
        }

        void AssertRead(WeaverPreparedToolInvocation read)
        {
            Assert.Equal(prepared.ApprovalDigest, read.ApprovalDigest);
            Assert.Equal(prepared.ApprovalDigest, WeaverInvocationIntegrity.PreparedDigest(read));
            if (classed) Assert.Equal(classes, read.RequiredPrivilegeClasses);
            else Assert.Null(read.RequiredPrivilegeClasses);
        }
    }
}
