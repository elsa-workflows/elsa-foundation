using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Reports durability for EF-owned checkpoint infrastructure, never for a process-only database.</summary>
internal sealed class EfRuntimeInfrastructureDurabilityEvidence(
    BookmarkStateDbContext context,
    string component) : IWorkflowDispatchDurabilityEvidence
{
    public string Component { get; } = component;

    public WorkflowDispatchDurabilityLevel Level
    {
        get
        {
            var provider = context.Database.ProviderName;
            if (provider is EfProviderNames.SqlServer or EfProviderNames.PostgreSql or EfProviderNames.MySql)
                return WorkflowDispatchDurabilityLevel.Durable;
            if (provider != EfProviderNames.Sqlite)
                return WorkflowDispatchDurabilityLevel.ProcessLocal;

            var connection = context.Database.GetDbConnection();
            var settings = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
            var dataSource = new[] { "Data Source", "DataSource", "Filename" }
                .Select(alias => settings.TryGetValue(alias, out var value) ? value?.ToString() : null)
                .FirstOrDefault(value => value is not null) ?? connection.DataSource;
            var mode = settings.TryGetValue("Mode", out var configuredMode) ? configuredMode?.ToString() : null;
            return string.Equals(mode, "Memory", StringComparison.OrdinalIgnoreCase) ||
                   string.IsNullOrWhiteSpace(dataSource) ||
                   dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
                   dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase) ||
                   dataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase)
                ? WorkflowDispatchDurabilityLevel.ProcessLocal
                : WorkflowDispatchDurabilityLevel.Durable;
        }
    }
}

internal static class EfRuntimeInfrastructureDurabilityEvidenceRegistration
{
    public static ServiceDescriptor AddOwned(IServiceCollection services, string component)
    {
        var descriptor = ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence>(provider =>
            new EfRuntimeInfrastructureDurabilityEvidence(provider.GetRequiredService<BookmarkStateDbContext>(), component));
        services.Add(descriptor);
        return descriptor;
    }
}
