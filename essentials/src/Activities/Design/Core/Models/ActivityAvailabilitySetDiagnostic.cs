namespace Elsa.Activities.Design.Core.Models;

public sealed record ActivityAvailabilitySetDiagnostic(string Name, IReadOnlyCollection<string> ActivityTypeKeys);
