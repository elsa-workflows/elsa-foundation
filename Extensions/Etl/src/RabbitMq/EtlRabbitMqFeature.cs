using CShells.Features;
using Elsa.Etl.Core.Runtime;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.RabbitMq;

/// <summary>Installs the RabbitMQ publisher that ETL RabbitMQ bindings resolve (spec 1200).</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "EtlRabbitMq", DisplayName = "ETL RabbitMQ delivery", Description = "ETL delivery to RabbitMQ exchanges, one persistent JSON message per record, recorded once the broker confirms every message.", DependsOn = new object[] { "EtlRuntime" })]
public class EtlRabbitMqFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // One instance for the process: it holds at most one blocked connection per target while a broker's resource alarm
        // lasts, and further publications to that target must see it.
        services.AddSingleton<IEtlRabbitMqPublisher, RabbitMqEtlPublisher>();
        services.AddSingleton<IEtlRabbitMqConsumer, RabbitMqEtlConsumer>();
    }
}
