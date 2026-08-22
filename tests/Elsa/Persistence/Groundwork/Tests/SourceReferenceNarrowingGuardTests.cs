using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using System.Reflection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// T145: every shipped source-reference reader must narrow by definition version itself, rather than inherit the
/// interface's unnarrowed default.
/// </summary>
/// <remarks>
/// <para>
/// <c>ListByDefinitionVersionPageAsync</c> is a defaulted member so in-process reader doubles stay compilable, and
/// it defaults to a <em>correct but unnarrowed</em> residual filter rather than to a throw, so a double handed to a
/// real consumer answers instead of faulting. That is deliberate, and it carries one hazard: the default is
/// silently <b>correct</b>, so a shipped store that forgets to override it does not fail \u2014 it just reads the whole
/// table. The export path did exactly that before T145 and nothing noticed for the life of the feature.
/// </para>
/// <para>
/// The hazard is therefore guarded where it lives. A test double inheriting the default is fine; a shipped store
/// doing so is the regression this catches. It lives in this project because it is the one that references the durable
/// provider assembly.
/// </para>
/// </remarks>
public sealed class SourceReferenceNarrowingGuardTests
{
    [Fact]
    public void Every_shipped_source_reference_reader_declares_its_own_definition_version_narrowing()
    {
        var contract = typeof(IWorkflowExecutableSourceReferenceReader);
        const string Member = "ListByDefinitionVersionPageAsync";

        // Scoped to the durable provider assembly on purpose. The unnarrowed default costs a table scan, which
        // matters for a persisted store and does not for an in-memory list; guarding where the cost is real keeps
        // this from becoming a rule that future in-process doubles have to argue with.
        var shipped = typeof(GroundworkWorkflowExecutableSourceReferenceStore).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(type))
            .ToArray();

        // The traversal must have reached the shipped store, or every assertion below is vacuous.
        Assert.Contains(shipped, type => type == typeof(GroundworkWorkflowExecutableSourceReferenceStore));

        var inheritingTheDefault = shipped
            .Where(type => type.GetMethod(
                Member,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is null)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            inheritingTheDefault.Length == 0,
            "These shipped readers inherit the unnarrowed default and will scan the whole source-reference table: "
            + string.Join(", ", inheritingTheDefault));
    }
}
