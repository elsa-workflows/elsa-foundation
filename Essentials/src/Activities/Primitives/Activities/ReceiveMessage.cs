using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>Authored business-message receiver. Runtime delivery participation is not enabled yet.</summary>
/// <remarks>Names and keys are exact text. Dynamic names require a finite literal declaration. This dormant
/// activity never creates a wait or treats a supplied trigger as accepted Message authority.</remarks>
[Activity(Icon = "Inbox", IsDiscoverable = false)]
[TriggerActivity]
[RefusesSecretBinding(nameof(MessageName), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(BusinessKey), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(DeclaredMessageNames), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(CanStartWorkflow), SecretBindingRefusalReason.FixedAtPublish)]
public sealed class ReceiveMessage : StatefulActivity<MessageReceived, ReceiveMessageWaitState, MessageReceived>
{
    public const string ActivityType = "Elsa.ReceiveMessage";
    public const string ResumeTargetId = "resume-target:message";

    [ActivityInput(Key = nameof(MessageName)), Required]
    public string MessageName { get; set; } = null!;

    [ActivityInput(Key = nameof(BusinessKey)), Required]
    public string BusinessKey { get; set; } = null!;

    [ActivityInput(Key = nameof(DeclaredMessageNames), DefaultValue = "[]", DefaultSyntax = "Object")]
    public ICollection<string> DeclaredMessageNames { get; set; } = [];

    [ActivityInput(Key = nameof(CanStartWorkflow), DisplayName = "Start new workflows", DefaultValue = "false", DefaultSyntax = "Literal")]
    public bool CanStartWorkflow { get; set; }

    // Guard before even interpreting a caller's start payload; no ordinary Start/Resume path can confer authority.
    protected override ValueTask<ActivityTransition<MessageReceived, ReceiveMessageWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        throw Unavailable();
    }

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<MessageReceived, ReceiveMessageWaitState>> ResumeAsync(
        ActivityResumeContext<ReceiveMessageWaitState, MessageReceived> context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        throw Unavailable();
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable() =>
        new("message.receive.unavailable", "This runtime cannot participate in durable Message delivery.");
}

/// <summary>Immutable typed Message data. Construction/deserialization proves neither acceptance nor delivery.</summary>
public sealed class MessageReceived
{
    public MessageReceived(string messageId, string messageName, string businessKey, JsonElement payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        // Reuse the SDK's structural text/payload validation and detachment, without calling a publisher.
        var content = new MessageSubmission(messageName, businessKey, payload);
        MessageId = messageId; MessageName = content.Name; BusinessKey = content.CorrelationKey; Payload = content.Payload;
    }

    [Output(Key = nameof(MessageId), Path = "messageId")]
    public string MessageId { get; }
    [Output(Key = nameof(MessageName), Path = "messageName")]
    public string MessageName { get; }
    [Output(Key = nameof(BusinessKey), Path = "businessKey")]
    public string BusinessKey { get; }
    [Output(Key = nameof(Payload), Path = "payload")]
    public JsonElement Payload { get; }
    public override string ToString() => "MessageReceived { [redacted] }";
}

/// <summary>Resolved private wait data, not a subscription, reservation or source capability.</summary>
public sealed class ReceiveMessageWaitState
{
    public ReceiveMessageWaitState(string messageName, string businessKey, IReadOnlyList<string>? declaredMessageNames = null)
    {
        var content = new MessageSubmission(messageName, businessKey, JsonSerializer.SerializeToElement<object?>(null));
        var declaration = new MessageMaterialInterestDeclaration("receive-message", declaredMessageNames ?? [messageName]);
        if (!declaration.MessageNames.Contains(messageName, StringComparer.Ordinal))
            throw new ArgumentException("The resolved Message name is outside its declaration.", nameof(messageName));
        MessageName = content.Name; BusinessKey = content.CorrelationKey; DeclaredMessageNames = declaration.MessageNames;
    }
    public string MessageName { get; }
    public string BusinessKey { get; }
    public IReadOnlyList<string> DeclaredMessageNames { get; }
    public override string ToString() => "ReceiveMessageWaitState { [redacted] }";
}
