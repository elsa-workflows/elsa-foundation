using CShells.Features;
using Elsa.Cluster.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

/// <summary>The generic effects contract over the Agent store's 1.1 data format.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature("FoundationAgentEfEffects", DisplayName = "Foundation Agent EF Effects",
    Description = "Enables generic effects when this host can use Agent 1.1 data in the selected durable store.",
    DependsOn = new object[] { typeof(FoundationAgentEntityFrameworkCoreFeature) })]
[RequiresSchemaVersion(AgentEfModule.SchemaFamily, "1.1.0")]
public class FoundationAgentEfEffectsFeature : FoundationAgentEffectsFeature;
