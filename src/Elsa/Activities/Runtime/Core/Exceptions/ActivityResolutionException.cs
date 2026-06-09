namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Domain-failure thrown when the activity factory or resolver registry cannot resolve a
/// descriptor's kind to a CLR type (e.g. unknown kind, missing module). Per Elsa §E2.6.1
/// this is a runtime/domain path failure, not a system failure — callers may catch and
/// translate to a graceful response.
/// </summary>
public sealed class ActivityResolutionException(string message) : Exception(message);
