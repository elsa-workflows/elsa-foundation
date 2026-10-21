using Elsa.Etl.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlApiCompositionTests
{
    [Fact]
    public void Independently_configured_api_owners_keep_their_own_cursor_keys_in_both_registration_orders()
    {
        foreach (var reverse in new[] { false, true })
        {
            var services = new ServiceCollection();
            var owners = new[] { (Name: "design", Key: new string('d', 32)), (Name: "runtime", Key: new string('r', 32)) };
            foreach (var owner in reverse ? owners.Reverse() : owners)
                EtlApiServices.Add(services, owner.Key, owner.Name);
            using var provider = services.BuildServiceProvider();
            var state = new EtlCursorState(1, "runs", new string('a', 64), null, 50, "run-a", 0, 0, null, null);
            var design = provider.GetRequiredKeyedService<EtlCursorCodec>("design");
            var runtime = provider.GetRequiredKeyedService<EtlCursorCodec>("runtime");
            Assert.Equal(state, runtime.Decode(new EtlCursorCodec(owners[1].Key).Encode(state)));
            Assert.Equal(state, design.Decode(new EtlCursorCodec(owners[0].Key).Encode(state)));
            Assert.Throws<FormatException>(() => runtime.Decode(design.Encode(state)));
            Assert.Throws<FormatException>(() => design.Decode(runtime.Encode(state)));
        }
    }
}
