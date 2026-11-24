using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;
using Elsa.Serialization.SystemText.Services;
using Elsa.Serialization.SystemText.Startup;
using Xunit;

namespace Elsa.Serialization.Tests;

public sealed class RuntimeTypeNullabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Competing_descriptor_null_contracts_and_CLR_types_are_refused(bool differentClr)
    {
        var providers = new IVariableTypeDescriptorProvider[] { new Provider(typeof(Uri), false), new Provider(differentClr ? typeof(string) : typeof(Uri), !differentClr) };
        Assert.Throws<DuplicateTypeAliasException>(() => new RuntimeTypeNullability(providers, new WellKnownTypeRegistry()));
    }

    [Fact]
    public async Task Competing_runtime_authorities_are_refused_at_startup()
    {
        var standard = new RuntimeTypeNullability([], new WellKnownTypeRegistry());
        var check = new RuntimeTypeNullabilityCompositionCheck([standard, new CompetingAuthority()]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.ExecuteAsync(default));
        Assert.Contains("Exactly one IRuntimeTypeNullability", error.Message);
        await new RuntimeTypeNullabilityCompositionCheck([standard]).ExecuteAsync(default);
    }

    private sealed class CompetingAuthority : IRuntimeTypeNullability
    {
        public bool TryGetSupportsNull(string alias, out bool supportsNull) { supportsNull = true; return true; }
    }
    private sealed class Provider(Type clrType, bool supportsNull) : IVariableTypeDescriptorProvider
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() =>
            [new("Acme.Reference", clrType, "Reference", "Test", "text", new HashSet<CollectionKind>([CollectionKind.Single]), supportsNull, true, new HashSet<string> { "elsa.json" })];
    }
}
