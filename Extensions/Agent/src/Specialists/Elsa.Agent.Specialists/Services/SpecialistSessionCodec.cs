using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.AI;
using static Elsa.Agent.Specialists.Services.SpecialistModelRunner;

namespace Elsa.Agent.Specialists.Services;

internal sealed record SpecialistSessionTarget(string ConversationId, long Revision);
internal sealed record SpecialistSessionReference(string ConversationId, long Revision, string ProtectedValue)
{
    public override string ToString() => $"SpecialistSessionReference {{ ConversationId = {ConversationId}, Revision = {Revision}, ProtectedValue = [protected] }}";
}

// Private portable history, never an SDK serialization format. The journal owns admission and
// atomically accepts this candidate with its validated result; a codec snapshot is not authority.
internal sealed class SpecialistSessionCodec(ISecretValueProtector protector, IPersistenceAccessContextAccessor access)
{
    internal const string Codec = "elsa.specialist.text-history/1";
    internal const int MaximumMessages = 64;
    internal const int MaximumSessionBytes = 8 * 1024 * 1024;
    private const string Purpose = "elsa.specialist.accepted-session/1";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(SpecialistJson.Options)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // This plaintext exists only inside authenticated protection, never in HTML. Avoid the
        // default HTML/Unicode escaping expansion when reserving bounded portable history bytes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal SpecialistSessionReference Capture(IReadOnlyList<ChatMessage> messages, SpecialistInvocationIdentity identity, SpecialistSessionTarget target)
    {
        EnsureScope(identity);
        EnsureTarget(target.ConversationId, target.Revision);
        var portable = Project(messages);
        Validate(portable, allowEmpty: false);
        var envelope = new SessionEnvelope(Purpose, Codec, identity.TenantId, identity.PersistencePartition,
            identity.DefinitionVersionId, identity.TemplateHash, target.ConversationId, target.Revision, portable);
        var encoded = JsonSerializer.Serialize(envelope, Json);
        if (Utf8.GetByteCount(encoded) > MaximumSessionBytes) throw Bounds();
        return new(target.ConversationId, target.Revision, protector.Protect(encoded));
    }

    internal IReadOnlyList<ChatMessage> Restore(SpecialistSessionReference reference, SpecialistInvocationIdentity identity)
    {
        // Scope selection is checked before touching protected data. The decoded resource pins must
        // still match; possession of a valid ciphertext never grants access to a different owner/version.
        EnsureScope(identity);
        EnsureTarget(reference.ConversationId, reference.Revision);
        SessionEnvelope? envelope;
        try
        {
            if (reference.ProtectedValue.Length > MaximumSessionBytes * 2) throw Bounds();
            var plaintext = protector.Unprotect(reference.ProtectedValue);
            if (Utf8.GetByteCount(plaintext) > MaximumSessionBytes) throw Bounds();
            using var document = JsonDocument.Parse(plaintext, new JsonDocumentOptions { MaxDepth = 32 });
            SpecialistJsonBounds.Check(document.RootElement, MaximumSessionBytes, 1024);
            envelope = document.RootElement.Deserialize<SessionEnvelope>(Json);
        }
        catch (SpecialistTurnException) { throw; }
        catch (Exception exception)
        {
            throw Failure("specialist.session.unavailable", "The retained specialist session cannot be read with the available codec and protection keys.", exception);
        }
        if (envelope is null || envelope.Purpose != Purpose || envelope.Codec != Codec)
            throw Failure("specialist.session.codec", "The retained specialist session uses an unsupported purpose or codec.");
        if (envelope.TenantId != identity.TenantId || envelope.Partition != identity.PersistencePartition ||
            envelope.DefinitionVersionId != identity.DefinitionVersionId || envelope.TemplateHash != identity.TemplateHash ||
            envelope.ConversationId != reference.ConversationId || envelope.Revision != reference.Revision)
            throw Failure("specialist.session.scope", "The retained specialist session does not belong to this owner and specialist version.");
        Validate(envelope.Messages, allowEmpty: false);
        return envelope.Messages.Select(message => new ChatMessage(new ChatRole(message.Role), message.Text)).ToArray();
    }

    internal void EnsureCapacity(IReadOnlyList<ChatMessage> history, string input, SpecialistInvocationIdentity identity, SpecialistSessionTarget target)
    {
        EnsureScope(identity);
        EnsureTarget(target.ConversationId, target.Revision);
        var messages = Project(history);
        Validate(messages, allowEmpty: true);
        // Reserve the worst-case JSON-escaped bounded result before dispatch. This is a byte/history
        // limit, not a claim that arbitrary provider input tokens have been accounted exactly.
        var used = JsonSerializer.SerializeToUtf8Bytes(messages, Json).Length;
        var incoming = JsonSerializer.SerializeToUtf8Bytes(new PortableMessage("user", input), Json).Length;
        var header = JsonSerializer.SerializeToUtf8Bytes(new SessionEnvelope(Purpose, Codec, identity.TenantId, identity.PersistencePartition,
            identity.DefinitionVersionId, identity.TemplateHash, target.ConversationId, target.Revision, []), Json).Length;
        if (messages.Count + 2 > MaximumMessages || used + incoming + header + 2L * SpecialistSchemaValidator.MaximumValueBytes + 64 > MaximumSessionBytes)
            throw Bounds();
    }

    internal static IReadOnlyList<ChatMessage> VisibleMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(message => new ChatMessage(message.Role,
            message.Contents.OfType<TextContent>().Select(content => (AIContent)new TextContent(content.Text)).ToList())).ToArray();

    private static IReadOnlyList<PortableMessage> Project(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count > MaximumMessages || messages.Any(message => message.Contents.Any(content => content is not TextContent))) throw Bounds();
        return messages.Select(message => new PortableMessage(message.Role.Value, message.Text)).ToArray();
    }

    private static void Validate(IReadOnlyList<PortableMessage>? messages, bool allowEmpty)
    {
        if (messages is null || messages.Count > MaximumMessages || messages.Count % 2 != 0 || !allowEmpty && messages.Count == 0)
            throw Bounds();
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (message is null || message.Role != (index % 2 == 0 ? "user" : "assistant") || message.Text is null) throw Bounds();
            try { if (Utf8.GetByteCount(message.Text) > SpecialistSchemaValidator.MaximumValueBytes) throw Bounds(); }
            catch (EncoderFallbackException exception) { throw Failure("specialist.session.bounds", "The retained specialist session contains invalid text.", exception); }
        }
    }

    private void EnsureScope(SpecialistInvocationIdentity identity)
    {
        try
        {
            if (access.Current.RequireScope().Value != identity.PersistencePartition || string.IsNullOrWhiteSpace(identity.DefinitionVersionId) || string.IsNullOrWhiteSpace(identity.TemplateHash))
                throw new InvalidOperationException("The session scope or specialist version is unavailable.");
            access.Current.EnsureTenantScope(identity.TenantId);
            foreach (var value in new[] { identity.TenantId, identity.PersistencePartition, identity.DefinitionVersionId, identity.TemplateHash })
                if (value is not null) _ = Utf8.GetByteCount(value);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw Failure("specialist.session.scope", "The specialist session requires its selected ordinary owner scope and exact version.", exception);
        }
    }

    private static void EnsureTarget(string? conversationId, long revision)
    {
        if (conversationId is null || conversationId.Length != 64 || conversationId.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) || revision < 1)
            throw Failure("specialist.session.reference", "The admitted specialist session has no valid conversation and revision binding.");
    }

    private static SpecialistTurnException Bounds() => Failure("specialist.session.bounds", "The specialist session exceeds the supported role, turn or byte profile; start an explicit fresh conversation.");
    private sealed record PortableMessage(string Role, string Text);
    private sealed record SessionEnvelope(string Purpose, string Codec, string? TenantId, string Partition, string DefinitionVersionId, string TemplateHash,
        string ConversationId, long Revision, IReadOnlyList<PortableMessage> Messages);
}
