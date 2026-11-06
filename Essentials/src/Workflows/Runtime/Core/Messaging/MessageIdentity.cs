using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Messaging;

/// <summary>Exact persistence, tenant and optional finite-test identity for a message operation.</summary>
/// <remarks>
/// This value is not an authorization capability. Providers must reread authoritative scope and test lifetime;
/// API boundaries must not trust a client-supplied scope. A null tenant never means across-scope access.
/// Execution partition and run kind are separately captured and validated, not additional production lanes.
/// Equality is ordinal; generated hash codes are for in-process collections only, never persisted identities.
/// </remarks>
public sealed record MessageScope
{
    public MessageScope(PersistenceScope persistenceScope, string? tenantId = null, string? testScopeId = null)
    {
        ArgumentNullException.ThrowIfNull(persistenceScope);
        MessageIdentityValidation.RequireText(persistenceScope.Value, nameof(persistenceScope));
        if (tenantId is not null) MessageIdentityValidation.RequireText(tenantId, nameof(tenantId));
        if (testScopeId is not null) MessageIdentityValidation.RequireText(testScopeId, nameof(testScopeId));
        PersistenceScope = persistenceScope; TenantId = tenantId; TestScopeId = testScopeId;
    }

    public PersistenceScope PersistenceScope { get; }
    public string? TenantId { get; }
    public string? TestScopeId { get; }
    public override string ToString() => "MessageScope { [redacted] }";
}

/// <summary>Stable logical definition identity, shared by all versions and execution models of one definition.</summary>
/// <remarks>
/// Ordinary authored native and BPMN versions use the same WorkflowDefinition namespace and canonical authored ID.
/// Standalone model namespaces require real owner authority, revalidated by the provider; hashes or protocol names
/// do not supply it. This value grants no publication/start authority. Generated hash codes are in-process only.
/// </remarks>
public sealed record MessageDefinitionIdentity
{
    public const string WorkflowDefinitionNamespace = "WorkflowDefinition";

    public MessageDefinitionIdentity(MessageScope scope, string definitionNamespace, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        MessageIdentityValidation.RequireText(definitionNamespace, nameof(definitionNamespace));
        MessageIdentityValidation.RequireText(definitionId, nameof(definitionId));
        Scope = scope; DefinitionNamespace = definitionNamespace; DefinitionId = definitionId;
    }

    public MessageScope Scope { get; }
    public string DefinitionNamespace { get; }
    public string DefinitionId { get; }

    public static MessageDefinitionIdentity ForWorkflowDefinition(MessageScope scope, string definitionId) =>
        new(scope, WorkflowDefinitionNamespace, definitionId);

    public override string ToString() => "MessageDefinitionIdentity { [redacted] }";
}

/// <summary>One message-started admission lane for a logical definition and exact business key.</summary>
/// <remarks>
/// No message name, version, execution model, artifact or execution partition splits this lane.
/// The value is not proof of reservation or active execution; providers must validate that authority separately.
/// String equality is ordinal and generated hash codes are in-process only, never durable key derivations.
/// </remarks>
public sealed record MessageStartAdmissionIdentity
{
    public MessageStartAdmissionIdentity(MessageDefinitionIdentity definition, string businessKey)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MessageIdentityValidation.RequireText(businessKey, nameof(businessKey));
        Definition = definition; BusinessKey = businessKey;
    }

    public MessageDefinitionIdentity Definition { get; }
    public string BusinessKey { get; }
    public override string ToString() => "MessageStartAdmissionIdentity { [redacted] }";
}

/// <summary>One accepted message's independent delivery identity within a logical definition.</summary>
/// <remarks>
/// The accepted-message ID must originate from server acceptance and be reread in its authoritative scope.
/// This value proves neither acceptance nor delivery and must not be trusted as client authority.
/// Equality is ordinal; generated hash codes are in-process only, never persisted identity derivations.
/// </remarks>
public sealed record MessageDefinitionDeliveryIdentity
{
    public MessageDefinitionDeliveryIdentity(string acceptedMessageId, MessageDefinitionIdentity definition)
    {
        MessageIdentityValidation.RequireText(acceptedMessageId, nameof(acceptedMessageId));
        ArgumentNullException.ThrowIfNull(definition);
        AcceptedMessageId = acceptedMessageId; Definition = definition;
    }

    public string AcceptedMessageId { get; }
    public MessageDefinitionIdentity Definition { get; }
    public override string ToString() => "MessageDefinitionDeliveryIdentity { [redacted] }";
}

internal static class MessageIdentityValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void RequireText(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A message identity value must be nonblank.", parameter);
        try { _ = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("A message identity value must contain valid Unicode.", parameter);
        }
    }
}
