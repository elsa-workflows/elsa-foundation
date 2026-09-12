using Elsa.Activities.Design.Api;
using Elsa.Agent.Anthropic;
using Elsa.Agent.GitHubCopilot;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Workbench;
using Elsa.Workflows.Design.Reconciliation.Git;
using Elsa.Workflows.Runtime.Api;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Pins every key, token, password and connection-string setting on a shipped feature as secret, as the feature catalog
/// reads it. Dropping one of these flags would put that value back into the catalog's responses.
/// </summary>
public sealed class SecretFeatureSettingTests
{
    public static TheoryData<Type, string> SecretSettings => new()
    {
        { typeof(GroundworkWorkflowRuntimeFeature), nameof(GroundworkWorkflowRuntimeFeature.RecoveryContinuationSigningKey) },
        { typeof(WorkflowsRuntimeApiFeature), nameof(WorkflowsRuntimeApiFeature.WorkflowAlterationPayloadProtectionKeys) },
        { typeof(WorkflowsRuntimeApiFeature), nameof(WorkflowsRuntimeApiFeature.ActivityExecutionHierarchyCursorSigningKey) },
        { typeof(ActivitiesDesignApiFeature), nameof(ActivitiesDesignApiFeature.DependencyCursorSigningKey) },
        { typeof(AspNetCoreIdentityGroundworkFeature), nameof(AspNetCoreIdentityGroundworkFeature.SeedAdminPassword) },
        { typeof(OpenIddictIdentityFeature), nameof(OpenIddictIdentityFeature.SigningKey) },
        { typeof(OpenIddictIdentityFeature), nameof(OpenIddictIdentityFeature.EncryptionKey) },
        { typeof(OpenIddictIdentityFeature), nameof(OpenIddictIdentityFeature.ConnectionString) },
        { typeof(AnthropicAgentFeature), nameof(AnthropicAgentFeature.ApiKey) },
        { typeof(GitHubCopilotAgentFeature), nameof(GitHubCopilotAgentFeature.GitHubToken) },
        { typeof(GitHubCopilotAgentFeature), nameof(GitHubCopilotAgentFeature.RuntimeConnectionToken) },
        { typeof(OpenTelemetryFeature), nameof(OpenTelemetryFeature.ApiKey) },
        { typeof(WorkflowsDesignGitReconciliationFeature), nameof(WorkflowsDesignGitReconciliationFeature.Token) },
        { typeof(SecretsEntityFrameworkCoreFeature), nameof(SecretsEntityFrameworkCoreFeature.ConnectionString) },
        { typeof(GroundworkSqliteProviderFeature), nameof(GroundworkSqliteProviderFeature.ConnectionString) }
    };

    [Theory]
    [MemberData(nameof(SecretSettings))]
    public void Setting_is_declared_secret(Type featureType, string setting) =>
        Assert.True(Assert.Single(ManifestHintReader.Read(featureType).Settings, x => x.Name == setting).Secret);
}
