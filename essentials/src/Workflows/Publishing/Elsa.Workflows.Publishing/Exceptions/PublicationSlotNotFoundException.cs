namespace Elsa.Workflows.Publishing.Exceptions;

/// <summary>
/// Raised when a publication slot lifecycle operation cannot find what it acts on: the slot itself, the retired
/// publication a restore would reinstate, or the executable artifact or source reference that publication points at.
/// </summary>
/// <remarks>
/// The unpublish and restore endpoints answer 404 for this type and for nothing else. The distinction lives in the
/// type, not in a parsed message, so rewording a handler message cannot turn a missing slot into a 500, and an
/// unrelated failure whose diagnostic happens to say "unavailable" cannot pass itself off as a 404.
/// </remarks>
public sealed class PublicationSlotNotFoundException(string message) : InvalidOperationException(message);
