using CShells.Features;
using Elsa.Cluster.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore;

/// <summary>The generic effects contract over the Weaver store's 1.1 data format.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature("WeaverEfEffects", DisplayName = "Foundation Weaver EF Effects",
    Description = "Enables generic effects when this host can use Weaver 1.1 data in the selected durable store.",
    DependsOn = new object[] { typeof(WeaverEntityFrameworkCoreFeature) })]
[RequiresSchemaVersion(WeaverEfModule.SchemaFamily, "1.1.0")]
public class WeaverEfEffectsFeature : WeaverEffectsFeature;
