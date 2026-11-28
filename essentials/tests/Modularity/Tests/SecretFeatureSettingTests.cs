using Elsa.Activities.Design.Api;
using Elsa.Weaver.Anthropic;
using Elsa.Weaver.GitHubCopilot;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Features;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Workbench;
using Elsa.Workflows.Design.Reconciliation.Git;
using Elsa.Workflows.Runtime.KernelHosting;
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
        { typeof(RuntimeArtifactsEntityFrameworkCoreFeature), nameof(RuntimeArtifactsEntityFrameworkCoreFeature.RecoveryContinuationSigningKey) },
        { typeof(RuntimeArtifactsEntityFrameworkCoreFeature), nameof(RuntimeArtifactsEntityFrameworkCoreFeature.ConnectionString) },
        { typeof(RuntimeWorkflowTestScopeEntityFrameworkCoreFeature), nameof(RuntimeWorkflowTestScopeEntityFrameworkCoreFeature.RecoveryContinuationSigningKey) },
        { typeof(RuntimeWorkflowTestScopeEntityFrameworkCoreFeature), nameof(RuntimeWorkflowTestScopeEntityFrameworkCoreFeature.ConnectionString) },
        { typeof(WorkflowsRuntimeKernelHostingFeature), nameof(WorkflowsRuntimeKernelHostingFeature.PostgresConnectionString) },
        { typeof(ActivitiesDesignApiFeature), nameof(ActivitiesDesignApiFeature.DependencyCursorSigningKey) },
        { typeof(AspNetCoreIdentityEntityFrameworkCoreFeature), nameof(AspNetCoreIdentityEntityFrameworkCoreFeature.SeedAdminPassword) },
        { typeof(OpenIddictIdentityFeature), nameof(OpenIddictIdentityFeature.SigningKey) },
        { typeof(OpenIddictIdentityFeature), nameof(OpenIddictIdentityFeature.EncryptionKey) },
        { typeof(OpenIddictIdentityFeature), nameof(OpenIddictIdentityFeature.ConnectionString) },
        { typeof(WeaverAnthropicFeature), nameof(WeaverAnthropicFeature.ApiKey) },
        { typeof(WeaverGitHubCopilotFeature), nameof(WeaverGitHubCopilotFeature.GitHubToken) },
        { typeof(WeaverGitHubCopilotFeature), nameof(WeaverGitHubCopilotFeature.RuntimeConnectionToken) },
        { typeof(OpenTelemetryFeature), nameof(OpenTelemetryFeature.ApiKey) },
        { typeof(WorkflowsDesignGitReconciliationFeature), nameof(WorkflowsDesignGitReconciliationFeature.Token) },
        { typeof(SecretsEntityFrameworkCoreFeature), nameof(SecretsEntityFrameworkCoreFeature.ConnectionString) },
        { typeof(SecretsFeature), nameof(SecretsFeature.EncryptionKey) },
        { typeof(SecretsFeature), nameof(SecretsFeature.Keys) }
    };

    [Theory]
    [MemberData(nameof(SecretSettings))]
    public void Setting_is_declared_secret(Type featureType, string setting) =>
        Assert.True(Assert.Single(ManifestHintReader.Read(featureType).Settings, x => x.Name == setting).Secret);
}
