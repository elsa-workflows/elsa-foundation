using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Records the exact three classic scope defaults a provider may replace together.</summary>
internal sealed record RuntimeDefaultTestScopeRegistrations(
    ServiceDescriptor Store,
    ServiceDescriptor Admission,
    ServiceDescriptor Cleanup);
