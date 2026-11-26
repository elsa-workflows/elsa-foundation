using System.Security.Cryptography;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowAlterationPayloadProtectionTests
{
    [Fact]
    public void Protector_BindsTenantAndAssociatedDataAndSupportsRestartStableKeys()
    {
        var options = Options.Create(new WorkflowAlterationPayloadProtectionOptions
        {
            ActiveKeyId = "key-1",
            Keys = new Dictionary<string, string> { ["key-1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
        });
        IWorkflowAlterationPayloadProtector first = new AesGcmWorkflowAlterationPayloadProtector(options, new WorkflowAlterationEphemeralKeyRing());
        var protectedPayload = first.Protect("plan-1", "tenant-a", "request-hash", "{\"value\":\"secret\"}");
        IWorkflowAlterationPayloadProtector restarted = new AesGcmWorkflowAlterationPayloadProtector(options, new WorkflowAlterationEphemeralKeyRing());

        Assert.Equal("{\"value\":\"secret\"}", restarted.Unprotect("plan-1", "tenant-a", "request-hash", protectedPayload));
        Assert.ThrowsAny<CryptographicException>(() => restarted.Unprotect("plan-1", "tenant-b", "request-hash", protectedPayload));
    }

    [Fact]
    public void Protector_RequiresConfiguredKeyWhenEphemeralDevelopmentModeIsDisabled()
    {
        var protector = new AesGcmWorkflowAlterationPayloadProtector(
            Options.Create(new WorkflowAlterationPayloadProtectionOptions()),
            new WorkflowAlterationEphemeralKeyRing());

        Assert.Throws<CryptographicException>(() => protector.Protect("plan-1", "tenant-a", "request-hash", "{}"));
    }

    [Fact]
    public void ProtectedPayload_DoesNotExposePlaintextInPlanShape()
    {
        var payload = new ProtectedWorkflowAlterationPayload("key-1", "A256GCM", "ciphertext");
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            "plan-1", new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator"), new WorkflowAlterationOperatorProvenance("operator", null),
            "idempotency", "request", payload, WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"]), DateTimeOffset.UnixEpoch);

        Assert.Equal("ciphertext", plan.ProtectedPayload.Ciphertext);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(plan), StringComparison.OrdinalIgnoreCase);
    }
}
