using System.Text.Json;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Requests;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Spec 150 Part A. Both defects share a shape: a published artifact was internally consistent and
/// reproducible, yet a consumer following the documented rule reached the wrong conclusion. Byte-level
/// freshness cannot see that class of defect, so it is pinned here.
/// </summary>
public sealed class PublishedAvailabilityTests
{
    private static string ContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    /// A1: Literal/Object/Input are gated by no feature, so a feature-keyed intersection drops them —
    /// which left Literal, used on nearly every authored input, unreachable through the documented
    /// availability rule even after it was published. Availability is therefore stated directly.
    /// </summary>
    [Fact]
    public void Hosts_publish_the_expression_types_they_serve_including_the_ungated_intrinsics()
    {
        using var hosts = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "hosts.json")));

        foreach (var host in hosts.RootElement.GetProperty("hosts").EnumerateArray())
        {
            var expressionTypes = host.GetProperty("expressionTypes").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();

            Assert.Equal(expressionTypes.OrderBy(type => type, StringComparer.Ordinal), expressionTypes);
            foreach (var intrinsic in new[] { "Literal", "Object", "Input" })
            {
                Assert.True(expressionTypes.Contains(intrinsic, StringComparer.Ordinal),
                    $"Host '{host.GetProperty("host").GetString()}' does not publish the ungated intrinsic expression type '{intrinsic}'; a consumer applying the documented availability rule would conclude it cannot author one.");
            }
        }
    }

    /// <summary>
    /// A2: AuthoredWorkflowIntrinsic's constructor requires a portable value type for every kind except
    /// control/finish, and a variable target for set/merge/reduce only. A plain type export cannot express
    /// a kind-dependent requirement, so the published schema accepted bodies the runtime refuses.
    /// </summary>
    [Fact]
    public void Submit_schema_expresses_the_kind_dependent_intrinsic_invariants()
    {
        var view = new GetWorkflowDefinitionSubmitSchemaHandler()
            .Handle(new GetWorkflowDefinitionSubmitSchema(), CancellationToken.None)
            .GetAwaiter().GetResult();

        var intrinsic = FindIntrinsic(view.Schema);
        Assert.True(intrinsic.HasValue, "The submit schema no longer exposes an intrinsic sub-schema to constrain.");

        var conditions = intrinsic!.Value.GetProperty("allOf").EnumerateArray().ToArray();
        Assert.Equal(4, conditions.Length);

        // A Set intrinsic must be required to carry both valueType and variable.
        Assert.Contains(conditions, condition => RequiresFor(condition, kind: "set", member: "valueType"));
        Assert.Contains(conditions, condition => RequiresFor(condition, kind: "set", member: "variable"));
        // A Finish intrinsic carries an outcome key, never a value type.
        Assert.Contains(conditions, condition => ForbidsFor(condition, kind: "finish", member: "valueType"));
        // Only variable-writing kinds may carry a variable target.
        Assert.Contains(conditions, condition => ForbidsFor(condition, kind: "setOutput", member: "variable"));
    }

    private static bool RequiresFor(JsonElement condition, string kind, string member) =>
        AppliesTo(condition, kind) &&
        condition.TryGetProperty("then", out var then) &&
        then.TryGetProperty("required", out var required) &&
        required.EnumerateArray().Any(value => value.GetString() == member);

    private static bool ForbidsFor(JsonElement condition, string kind, string member) =>
        AppliesTo(condition, kind) &&
        condition.TryGetProperty("then", out var then) &&
        then.TryGetProperty("properties", out var properties) &&
        properties.TryGetProperty(member, out var constrained) &&
        constrained.TryGetProperty("type", out var type) &&
        type.GetString() == "null";

    /// <summary>Evaluates the condition's <c>if</c> (including a negated one) against a concrete kind.</summary>
    private static bool AppliesTo(JsonElement condition, string kind)
    {
        if (!condition.TryGetProperty("if", out var clause))
            return false;

        var negated = clause.TryGetProperty("not", out var inner);
        var match = negated ? inner : clause;
        if (!match.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty("kind", out var kindSchema) ||
            !kindSchema.TryGetProperty("enum", out var members))
            return false;

        var listed = members.EnumerateArray().Any(value => value.GetString() == kind);
        return negated ? !listed : listed;
    }

    private static JsonElement? FindIntrinsic(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties))
            {
                if (properties.TryGetProperty("intrinsic", out var intrinsic) && intrinsic.TryGetProperty("allOf", out _))
                    return intrinsic;

                foreach (var property in properties.EnumerateObject())
                {
                    if (FindIntrinsic(property.Value) is { } found)
                        return found;
                }
            }

            foreach (var member in schema.EnumerateObject().Where(member => member.Name != "properties"))
            {
                if (FindIntrinsic(member.Value) is { } found)
                    return found;
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (FindIntrinsic(item) is { } found)
                    return found;
            }
        }

        return null;
    }
}
