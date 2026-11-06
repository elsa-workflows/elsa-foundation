using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class MessageIdentityTests
{
    [Fact]
    public void Reconstructed_values_share_equality_operators_and_in_process_collection_membership()
    {
        var first = new MessageScope(new PersistenceScope("store"), "tenant", "test");
        var second = new MessageScope(new PersistenceScope("store"), "tenant", "test");
        Assert.True(first == second);
        Assert.False(first != second);
        var firstDefinition = MessageDefinitionIdentity.ForWorkflowDefinition(first, "orders");
        var secondDefinition = MessageDefinitionIdentity.ForWorkflowDefinition(second, "orders");
        Assert.Equal(firstDefinition, secondDefinition);
        Assert.Single(new HashSet<MessageStartAdmissionIdentity>
        {
            new(firstDefinition, "123"), new(secondDefinition, "123")
        });
        Assert.Single(new HashSet<MessageDefinitionDeliveryIdentity>
        {
            new("accepted", firstDefinition), new("accepted", secondDefinition)
        });
    }

    [Theory]
    [InlineData("persistence")]
    [InlineData("tenant")]
    [InlineData("test")]
    [InlineData("production")]
    public void Every_scope_dimension_separates_delivery_and_admission(string dimension)
    {
        var original = new MessageScope(new PersistenceScope("store"), "tenant", "test");
        var other = dimension switch
        {
            "persistence" => new MessageScope(new PersistenceScope("another"), "tenant", "test"),
            "tenant" => new MessageScope(new PersistenceScope("store"), "another", "test"),
            "test" => new MessageScope(new PersistenceScope("store"), "tenant", "another"),
            _ => new MessageScope(new PersistenceScope("store"), "tenant")
        };
        var definition = MessageDefinitionIdentity.ForWorkflowDefinition(original, "orders");
        var otherDefinition = MessageDefinitionIdentity.ForWorkflowDefinition(other, "orders");
        Assert.NotEqual(original, other);
        Assert.NotEqual(new MessageStartAdmissionIdentity(definition, "123"), new MessageStartAdmissionIdentity(otherDefinition, "123"));
        Assert.NotEqual(new MessageDefinitionDeliveryIdentity("accepted", definition), new MessageDefinitionDeliveryIdentity("accepted", otherDefinition));
    }

    [Fact]
    public void Workflow_definition_factory_uses_the_canonical_authored_namespace()
    {
        var scope = Scope();
        var nativeVersion = MessageDefinitionIdentity.ForWorkflowDefinition(scope, "orders");
        var bpmnVersion = MessageDefinitionIdentity.ForWorkflowDefinition(scope, "orders");
        Assert.Equal(new MessageDefinitionIdentity(scope, "WorkflowDefinition", "orders"), nativeVersion);
        Assert.Equal(MessageDefinitionIdentity.WorkflowDefinitionNamespace, nativeVersion.DefinitionNamespace);
        Assert.Equal(new MessageStartAdmissionIdentity(nativeVersion, "123"), new MessageStartAdmissionIdentity(bpmnVersion, "123"));
        Assert.Equal(new MessageDefinitionDeliveryIdentity("accepted", nativeVersion), new MessageDefinitionDeliveryIdentity("accepted", bpmnVersion));
    }

    [Fact]
    public void Explicit_standalone_namespace_does_not_alias_an_authored_definition_with_the_same_ID()
    {
        var authored = MessageDefinitionIdentity.ForWorkflowDefinition(Scope(), "orders");
        var standalone = new MessageDefinitionIdentity(Scope(), "StandaloneOwner", "orders");
        Assert.NotEqual(authored, standalone);
        Assert.NotEqual(new MessageStartAdmissionIdentity(authored, "123"), new MessageStartAdmissionIdentity(standalone, "123"));
    }

    [Theory]
    [InlineData("Orders", "orders")]
    [InlineData("123", "0123")]
    [InlineData("orders", " orders ")]
    [InlineData("é", "é")]
    [InlineData("ab:c", "a:bc")]
    public void Exact_identity_text_is_not_trimmed_folded_normalized_or_parsed(string left, string right)
    {
        var scope = Scope();
        var definition = MessageDefinitionIdentity.ForWorkflowDefinition(scope, "orders");
        Assert.NotEqual(new MessageScope(new PersistenceScope("store"), left), new MessageScope(new PersistenceScope("store"), right));
        Assert.NotEqual(new MessageDefinitionIdentity(scope, left, "orders"), new MessageDefinitionIdentity(scope, right, "orders"));
        Assert.NotEqual(MessageDefinitionIdentity.ForWorkflowDefinition(scope, left), MessageDefinitionIdentity.ForWorkflowDefinition(scope, right));
        Assert.NotEqual(new MessageStartAdmissionIdentity(definition, left), new MessageStartAdmissionIdentity(definition, right));
        Assert.NotEqual(new MessageDefinitionDeliveryIdentity(left, definition), new MessageDefinitionDeliveryIdentity(right, definition));
    }

    [Fact]
    public void Separate_components_cannot_alias_by_concatenation_or_missing_attribution()
    {
        Assert.NotEqual(new MessageDefinitionIdentity(Scope(), "ab", "c"), new MessageDefinitionIdentity(Scope(), "a", "bc"));
        Assert.NotEqual(new MessageScope(new PersistenceScope("store")), new MessageScope(new PersistenceScope("store"), "tenant"));
        Assert.NotEqual(new MessageScope(new PersistenceScope("store")), new MessageScope(new PersistenceScope("another")));
        var first = new MessageDefinitionIdentity(Scope(), "owner", "bc");
        var second = new MessageDefinitionIdentity(Scope(), "owner", "c");
        Assert.NotEqual(new MessageDefinitionDeliveryIdentity("a", first), new MessageDefinitionDeliveryIdentity("ab", second));
        Assert.NotEqual(new MessageScope(new PersistenceScope("a\0b"), "c"), new MessageScope(new PersistenceScope("a"), "b\0c"));
        Assert.NotEqual(new MessageDefinitionIdentity(Scope(), "a\0b", "c"), new MessageDefinitionIdentity(Scope(), "a", "b\0c"));
        Assert.NotEqual(new MessageDefinitionDeliveryIdentity("a\0b", second), new MessageDefinitionDeliveryIdentity("a\0", first));
    }

    [Fact]
    public void Optional_null_attribution_is_valid_and_never_inferred_from_the_persistence_value()
    {
        var scope = new MessageScope(new PersistenceScope("tenant-looking-store"), null, null);
        Assert.Equal(new MessageScope(new PersistenceScope("tenant-looking-store")), scope);
        Assert.Null(scope.TenantId);
        Assert.Null(scope.TestScopeId);
        Assert.NotEqual(scope, new MessageScope(scope.PersistenceScope, "tenant-looking-store"));
    }

    [Theory]
    [InlineData("persistence")]
    [InlineData("tenant")]
    [InlineData("test")]
    [InlineData("namespace")]
    [InlineData("definition")]
    [InlineData("key")]
    [InlineData("accepted-message")]
    public void Null_and_blank_identities_refuse_and_business_keys_remain_portable(string field)
    {
        string?[] invalid = field switch
        {
            "tenant" or "test" => ["", "\t\u2003"],
            "key" => [null, "", "\t\u2003", "private-identity-canary-\ud800"],
            _ => [null, "", "\t\u2003"]
        };
        foreach (var text in invalid)
        {
            var error = Assert.ThrowsAny<ArgumentException>(() => WithText(field, text));
            Assert.DoesNotContain("private-identity-canary", error.ToString());
            Assert.Null(error.InnerException);
        }
    }

    [Theory]
    [InlineData("persistence")]
    [InlineData("tenant")]
    [InlineData("test")]
    [InlineData("namespace")]
    [InlineData("definition")]
    [InlineData("accepted-message")]
    public void Server_owned_components_preserve_exact_opaque_UTF16_without_aliasing(string field)
    {
        string[] values = ["opaque-\ud800", "opaque-\ud801", "opaque-\udc00", "opaque-\ufffd", "opaque\0tail", "opaque", "opaque\0other"];
        var identities = values.Select(value => WithText(field, value)).ToArray();
        Assert.Equal(values.Length, new HashSet<object>(identities).Count);
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(identities[i], WithText(field, values[i]));
            var retained = identities[i] switch
            {
                MessageScope scope => field == "persistence" ? scope.PersistenceScope.Value : field == "tenant" ? scope.TenantId : scope.TestScopeId,
                MessageDefinitionIdentity definition => field == "namespace" ? definition.DefinitionNamespace : definition.DefinitionId,
                MessageDefinitionDeliveryIdentity delivery => delivery.AcceptedMessageId,
                _ => throw new InvalidOperationException("Unexpected identity fixture.")
            };
            Assert.Equal(values[i], retained);
        }
    }

    [Fact]
    public void Null_component_references_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new MessageScope(null!));
        Assert.Throws<ArgumentNullException>(() => new MessageDefinitionIdentity(null!, "owner", "orders"));
        Assert.Throws<ArgumentNullException>(() => new MessageStartAdmissionIdentity(null!, "123"));
        Assert.Throws<ArgumentNullException>(() => new MessageDefinitionDeliveryIdentity("accepted", null!));
    }

    [Fact]
    public void Ordinary_formatting_of_any_identity_never_discloses_its_raw_components()
    {
        var privateValues = new[] { "private-store", "private-tenant", "private-test", "private-owner", "private-definition", "private-key", "private-message" };
        var scope = new MessageScope(new PersistenceScope(privateValues[0]), privateValues[1], privateValues[2]);
        var definition = new MessageDefinitionIdentity(scope, privateValues[3], privateValues[4]);
        object[] identities = [scope, definition, new MessageStartAdmissionIdentity(definition, privateValues[5]), new MessageDefinitionDeliveryIdentity(privateValues[6], definition)];
        foreach (var identity in identities)
        foreach (var value in privateValues)
        {
            Assert.DoesNotContain(value, $"Identity: {identity}");
            Assert.DoesNotContain(value, string.Format("{0}", identity));
        }
    }

    private static MessageScope Scope() => new(new PersistenceScope("store"), "tenant");

    private static object WithText(string field, string? value) => field switch
    {
        "persistence" => new MessageScope(new PersistenceScope(value!)),
        "tenant" => new MessageScope(new PersistenceScope("store"), value),
        "test" => new MessageScope(new PersistenceScope("store"), testScopeId: value),
        "namespace" => new MessageDefinitionIdentity(Scope(), value!, "orders"),
        "definition" => MessageDefinitionIdentity.ForWorkflowDefinition(Scope(), value!),
        "key" => new MessageStartAdmissionIdentity(MessageDefinitionIdentity.ForWorkflowDefinition(Scope(), "orders"), value!),
        _ => new MessageDefinitionDeliveryIdentity(value!, MessageDefinitionIdentity.ForWorkflowDefinition(Scope(), "orders"))
    };
}
