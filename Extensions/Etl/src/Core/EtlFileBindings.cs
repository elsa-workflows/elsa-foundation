namespace Elsa.Etl.Core;

/// <summary>Host-configured aliases used by the supported finite file pipeline.</summary>
/// <param name="Source">The approved finite CSV source alias.</param>
/// <param name="Lookup">The approved local CSV lookup alias.</param>
/// <param name="Output">The approved JSONL main-output alias.</param>
/// <param name="Errors">The approved JSONL error-output alias.</param>
public sealed record EtlFileBindings(string Source, string Lookup, string Output, string Errors);
