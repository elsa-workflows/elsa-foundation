using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.CredentialLiteralTestSupport;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// The credential-literal validator (spec 188, FR-008): it finds a refused binding at any depth, skips what it cannot
/// judge (intrinsics and activity versions the catalog does not hold), reports the contract's path, type and message
/// without the bound value, and returns the same findings through each way it is reached.
/// </summary>
public sealed class CredentialLiteralValidatorTests
{
    private readonly StubActivityCatalog _catalog = Catalog();
    private readonly CredentialLiteralValidator _validator;

    public CredentialLiteralValidatorTests() => _validator = Validator(_catalog);

    [Fact]
    public async Task A_literal_on_a_credential_input_is_reported_with_the_contracts_path_type_and_message()
    {
        var finding = Assert.Single(await _validator.Validate(CredentialBoundAs("Literal"), CancellationToken.None));

        Assert.Equal($"{NodeId}/inputs/{CredentialKey}", finding.Path);
        Assert.Equal("Inputs/CredentialLiteral", finding.Type);
        Assert.Equal(
            $"Inputs/CredentialLiteral: input '{CredentialName}' on activity '{NodeId}' holds a credential and accepts only a secret reference.",
            finding.Message);
        Assert.DoesNotContain(Literal, finding.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Variable")]
    [InlineData("JavaScript")]
    public async Task Any_other_value_or_expression_on_a_credential_input_is_reported(string binding)
    {
        var finding = Assert.Single(await _validator.Validate(CredentialBoundAs(binding), CancellationToken.None));

        Assert.StartsWith("Inputs/CredentialLiteral", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Literal, finding.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Secret")]
    [InlineData("EmptyLiteral")]
    public async Task A_secret_reference_or_an_empty_literal_on_a_credential_input_is_accepted(string binding) =>
        Assert.Empty(await _validator.Validate(CredentialBoundAs(binding), CancellationToken.None));

    [Theory]
    [InlineData(SensitiveKey)]
    [InlineData(PlainKey)]
    public async Task A_literal_on_an_input_that_is_not_a_credential_is_accepted(string inputKey) =>
        Assert.Empty(await _validator.Validate(State(ActivityVersionId, Bind(inputKey, "Literal")), CancellationToken.None));

    [Fact]
    public async Task A_refused_binding_nested_under_another_activity_is_found()
    {
        var nested = new ActivityNode("nested", ActivityVersionId, [Bind(CredentialKey, "Literal")], []);
        var state = State(activities: [Node("outer", ActivityVersionId, childActivities: [nested])]);

        var finding = Assert.Single(await _validator.Validate(state, CancellationToken.None));

        Assert.Equal($"nested/inputs/{CredentialKey}", finding.Path);
    }

    [Fact]
    public async Task A_node_whose_activity_version_the_catalog_does_not_hold_is_skipped() =>
        Assert.Empty(await _validator.Validate(State(UncatalogedActivityVersionId, Bind(CredentialKey, "Literal")), CancellationToken.None));

    [Fact]
    public async Task An_intrinsic_node_is_skipped_even_when_its_version_id_names_a_credential_activity()
    {
        var intrinsic = new ActivityNode(NodeId, ActivityVersionId, [Bind(CredentialKey, "Literal")], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Return, new TypeReference("String"))
        };

        Assert.Empty(await _validator.Validate(StateWithRoot(intrinsic), CancellationToken.None));
    }

    [Fact]
    public async Task The_same_findings_come_back_through_every_way_the_rule_is_reached()
    {
        var state = CredentialBoundAs("Literal");
        var expected = await _validator.Validate(state, CancellationToken.None);

        Assert.Single(expected);
        Assert.Equal(expected, await Validate(_validator, state));
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() => _validator.AdmitAsync(state, CancellationToken.None));
        Assert.Equal(expected, refusal.Findings);
        Assert.StartsWith("Inputs/CredentialLiteral", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Literal, refusal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admission_of_an_accepted_state_does_not_throw() =>
        Assert.Null(await Record.ExceptionAsync(() => _validator.AdmitAsync(CredentialBoundAs("Secret"), CancellationToken.None)));

    [Fact]
    public void A_finding_is_keyed_by_the_input_reference_key_and_names_the_input()
    {
        var finding = CredentialLiteralFinding.For("node-1", "key-1", "Display");

        Assert.Equal(new ValidationError(
            "node-1/inputs/key-1",
            "Inputs/CredentialLiteral",
            "Inputs/CredentialLiteral: input 'Display' on activity 'node-1' holds a credential and accepts only a secret reference."), finding);
    }
}
