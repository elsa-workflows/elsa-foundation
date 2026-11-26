namespace Elsa.Activities.Bpmn.Core;

/// <summary>
/// Invalid authored BPMN document source. Parser boundaries retain the infrastructure cause as
/// <see cref="Exception.InnerException"/> and name the owning node/process or view/scope in the message.
/// Deriving from <see cref="InvalidOperationException"/> preserves generic Design/publication refusal handling.
/// </summary>
public sealed class BpmnDocumentSourceException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
