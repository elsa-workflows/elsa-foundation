using System.Reflection;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The credential-literal rule (spec 188, FR-008, T055) runs only in the application layer. These guards keep its
/// compiled contract in shape: every design command contract is classified as admitted, exempt or not state-writing,
/// and promotion cannot be called without the admitted draft's hash.
/// </summary>
/// <remarks>
/// Whether a caller calls the helper before its command is proved per entry point by the behavioral tests (T051 to T054,
/// and the Elsa 3 import's own tests).
/// </remarks>
public sealed partial class ArchitectureGuardTests
{
    private const string ContractsNamespace = "Elsa.Workflows.Design.Persistence.Core.Contracts";

    /// <summary>Design commands that write workflow state: every caller admits that state first.</summary>
    private static readonly Type[] AdmittedCommands =
    [
        typeof(IAddWorkflowDefinitionCommand),
        typeof(IUpdateDraftCommand),
        typeof(IAddWorkflowDefinitionVersionCommand),
        typeof(ISubmitWorkflowDefinitionCommand),
        typeof(IPromoteDraftToVersionCommand),
        typeof(IMaterializeWorkflowDefinitionVersionCommand),
        typeof(ICreateDraftCommand)
    ];

    /// <summary>Design commands that write workflow state but are exempt, each with the reason.</summary>
    private static readonly Dictionary<Type, string> ExemptCommands = new()
    {
        [typeof(ICloneDraftFromVersionCommand)] =
            "copies a stored version into a draft and adds no content; a copied credential literal is refused when the draft is saved, promoted or published"
    };

    /// <summary>Design commands that write no workflow state.</summary>
    private static readonly Type[] NotStateWritingCommands =
    [
        typeof(ISaveWorkflowDefinitionCommand),
        typeof(IMaterializeWorkflowDefinitionCommand),
        typeof(IDiscardDraftCommand),
        typeof(IDeleteWorkflowDefinitionPermanentlyCommand)
    ];

    [Fact]
    public void Every_design_command_contract_is_classified_once()
    {
        var classified = AdmittedCommands.Concat(ExemptCommands.Keys).Concat(NotStateWritingCommands).ToArray();
        var contracts = typeof(IUpdateDraftCommand).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.Namespace == ContractsNamespace && type.Name.EndsWith("Command", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(classified.Length, classified.Distinct().Count());
        Assert.Equal(
            contracts.Select(type => type.Name).Order(StringComparer.Ordinal),
            classified.Select(type => type.Name).Order(StringComparer.Ordinal));
        Assert.Contains(typeof(IPromoteDraftToVersionCommand), AdmittedCommands);
    }

    [Fact]
    public void Every_promotion_requires_the_admitted_drafts_state_hash()
    {
        var context = new NullabilityInfoContext();
        var methods = typeof(IPromoteDraftToVersionCommand).GetMethods().Where(method => method.Name == "Execute").ToArray();
        var violations = methods
            .Where(method => method.GetParameters().SingleOrDefault(parameter => parameter.Name == "expectedStateHash") is not { } hash
                             || hash.ParameterType != typeof(string)
                             || context.Create(hash).WriteState != NullabilityState.NotNull)
            .Select(method => $"{method} does not take a non-nullable string expectedStateHash.")
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
}
