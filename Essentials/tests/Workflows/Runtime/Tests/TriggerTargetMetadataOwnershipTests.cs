using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TriggerTargetMetadataOwnershipTests
{
    [Fact]
    public void Contribution_lifetime_does_not_erase_supported_profiles_during_initialization()
    {
        var source = new BorrowedProfiles();
        var failure = Record.Exception(() => new TriggerTargetResolver(new InMemoryWorkflowActivationAuthority(),
            new InMemoryWorkflowExecutableSourceReferenceStore(), new InMemoryWorkflowExecutableStore(),
            new Access(), [source], TimeProvider.System));
        Assert.Empty(source.Profiles); // The producer resumed and discarded its borrowed collection.
        Assert.Null(failure);
    }

    private sealed class BorrowedProfiles : ITriggerTargetModelSource
    {
        internal List<string> Profiles { get; } = ["direct-v1"];
        public IEnumerable<TriggerTargetModelDescriptor> GetModels()
        {
            yield return new("test.model", "Test.Model", 1, Profiles, false);
            Profiles.Clear();
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("metadata-scope")); }
}
