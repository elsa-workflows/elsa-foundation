using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public void Connector_effect_identity_binds_batch_route_destination_and_exact_payload()
    {
        var admission = ConnectorAdmission("output-identity", new("tenant-a", "output-identity"), httpShardIds: ["shard"]);
        var batchId = new string('a', 64);
        var hash = new string('b', 64);
        var identity = EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, EtlOutputRoute.Main, hash, 50, 2);
        Assert.Equal(identity, EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, EtlOutputRoute.Main, hash, 50, 2));
        Assert.NotEqual(identity, EtlRuntimeIdentity.ConnectorEffectId(admission, new string('c', 64), EtlOutputRoute.Main, hash, 50, 2));
        Assert.NotEqual(identity, EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, EtlOutputRoute.Error, hash, 50, 2));
        Assert.NotEqual(identity, EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, EtlOutputRoute.Main, new string('d', 64), 50, 2));
        Assert.NotEqual(identity, EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, EtlOutputRoute.Main, hash, 51, 2));
        Assert.NotEqual(identity, EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, EtlOutputRoute.Main, hash, 50, 3));
        var repinned = admission with { Bindings = admission.Bindings with
        {
            Main = admission.Bindings.Main with { Revision = admission.Bindings.Main.Revision with { Revision = "new" } }
        } };
        Assert.NotEqual(identity, EtlRuntimeIdentity.ConnectorEffectId(repinned, batchId, EtlOutputRoute.Main, hash, 50, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, (EtlOutputRoute)99, hash, 50, 2));
    }
}
