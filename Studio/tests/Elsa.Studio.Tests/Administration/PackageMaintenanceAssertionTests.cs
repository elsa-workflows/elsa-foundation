using System.Text.Json.Nodes;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageMaintenanceAssertionTests
{
    [Fact]
    public void Completed_still_passes_without_changing_the_result()
    {
        var result = new JsonObject { ["status"] = "Completed" };
        var before = result.ToJsonString();
        PackageMaintenanceAssertions.Completed(result, PackageMaintenancePhase.KeepAll);
        Assert.Equal(before, result.ToJsonString());
    }

    [Theory]
    [InlineData("Refused")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Preview")]
    [InlineData(null)]
    [InlineData("Completed\nprivate-status-sentinel")]
    public void Every_non_completed_status_still_fails(string? status)
    {
        var result = new JsonObject { ["status"] = status };
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            PackageMaintenanceAssertions.Completed(result, PackageMaintenancePhase.PreserveRetainedGeneration));
        Assert.Contains("phase=preserve-retained-generation", error.Message);
        Assert.DoesNotContain("private-status-sentinel", error.Message);
    }

    [Theory]
    [InlineData(PackageStoreAdmissionReason.UnknownAuthority)]
    [InlineData(PackageStoreAdmissionReason.UnsupportedFilesystem)]
    [InlineData(PackageStoreAdmissionReason.RootMismatch)]
    [InlineData(PackageStoreAdmissionReason.IncompleteEnrollment)]
    [InlineData(PackageStoreAdmissionReason.StateMismatch)]
    [InlineData(PackageStoreAdmissionReason.ExpiredScope)]
    [InlineData(PackageStoreAdmissionReason.UnsupportedParticipant)]
    public void Refusal_failure_retains_the_typed_reason_and_seeded_context(PackageStoreAdmissionReason reason)
    {
        var result = new JsonObject
        {
            ["status"] = "Refused", ["protectionKnowledge"] = "Unknown", ["inventoryComplete"] = false,
            ["diagnostics"] = new JsonArray(new JsonObject { ["code"] = reason.ToString(), ["message"] = "private-message-sentinel" })
        };
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            PackageMaintenanceAssertions.Completed(result, PackageMaintenancePhase.PreserveRetainedGeneration));
        Assert.Contains("status=Refused", error.Message);
        Assert.Contains($"refusalCodes=[{reason}]", error.Message);
        Assert.Contains("seededVersions=1.0.0,2.0.0; retainedShell=1.0.0", error.Message);
        Assert.Contains("protectionKnowledge=Unknown; inventoryComplete=false", error.Message);
        Assert.DoesNotContain("private-message-sentinel", error.Message);
    }

    [Fact]
    public void Private_fields_unknown_tokens_and_excess_diagnostics_are_bounded_and_omitted()
    {
        var privateValue = "private-sentinel\n" + new string('x', 10_000);
        var diagnostics = new JsonArray();
        for (var i = 0; i < 100; i++)
            diagnostics.Add(new JsonObject { ["code"] = privateValue, ["message"] = privateValue });
        var result = new JsonObject
        {
            ["status"] = "Refused", ["protectionKnowledge"] = privateValue,
            ["rootLabel"] = privateValue, ["rootIdentity"] = privateValue,
            ["inventory"] = privateValue, ["uses"] = privateValue, ["candidates"] = privateValue,
            ["correlationId"] = privateValue, ["diagnostics"] = diagnostics
        };
        var before = result.ToJsonString();
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            PackageMaintenanceAssertions.Completed(result, PackageMaintenancePhase.PreserveRetainedGeneration));
        Assert.Contains("refusalCodes=[OtherOrMissing]", error.Message);
        Assert.Contains("diagnosticsTruncated=True", error.Message);
        Assert.DoesNotContain("private-sentinel", error.Message);
        Assert.True(error.Message.Length < 600);
        Assert.Equal(before, result.ToJsonString());
    }

    [Fact]
    public void Unexpected_diagnostic_shapes_and_numeric_enum_tokens_do_not_mask_the_refusal()
    {
        var result = new JsonObject
        {
            ["status"] = "Refused", ["inventoryComplete"] = new JsonObject(),
            ["diagnostics"] = new JsonArray(null, new JsonArray(), new JsonObject { ["code"] = 0 },
                new JsonObject { ["code"] = "0" }, new JsonObject { ["code"] = " UnknownAuthority" },
                new JsonObject { ["code"] = "UnknownAuthority" })
        };
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            PackageMaintenanceAssertions.Completed(result, (PackageMaintenancePhase)999));
        Assert.Contains("phase=unknown", error.Message);
        Assert.Contains("refusalCodes=[OtherOrMissing,UnknownAuthority]", error.Message);
        Assert.Contains("inventoryComplete=unknown", error.Message);
    }
}
