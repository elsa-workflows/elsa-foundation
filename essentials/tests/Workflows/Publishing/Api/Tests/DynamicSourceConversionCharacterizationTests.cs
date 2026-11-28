using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Characterises, on main before #4078, what publication's conversion planner and the runtime conversion executor do
/// with a source whose contract is dynamic, and with an identity plan whose payload does not have the JSON shape of
/// its source type. A member read deferred to run time (#4078 "DeferredFit") has no static source contract, and a
/// known one is planned from shape facts, so these decide what the new fit check has to add.
/// </summary>
public sealed class DynamicSourceConversionCharacterizationTests
{
    private readonly ValueConversionPlanResolver _resolver = new();

    [Theory]
    [InlineData("Elsa.Any")]
    [InlineData("Object")]
    [InlineData("System.Text.Json.JsonElement")]
    public void A_dynamic_source_cannot_be_planned_onto_a_string_target(string sourceAlias)
    {
        var source = new ValueTypeDescriptor(sourceAlias);

        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            _resolver.Resolve(source, ValueRepresentationDefaults.Infer(source), new ValueTypeDescriptor("String")));

        Assert.Contains("VF-COER", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dynamic_source_onto_a_dynamic_target_is_planned_without_a_static_contract()
    {
        var source = new ValueTypeDescriptor("Elsa.Any");

        var plan = _resolver.Resolve(source, ValueRepresentationDefaults.Infer(source), source);

        Assert.Equal(ValueConversionOperation.Identity, plan.Operation);
    }

    /// <summary>
    /// An identity plan checks the envelope's type label, never the payload: a JSON number or object labelled String
    /// passes through unchanged. Runtime resolution labels every source with the plan's source type before converting,
    /// so a wrong payload under a Known member plan would reach the input snapshot unless #4078's fit check stops it.
    /// </summary>
}
