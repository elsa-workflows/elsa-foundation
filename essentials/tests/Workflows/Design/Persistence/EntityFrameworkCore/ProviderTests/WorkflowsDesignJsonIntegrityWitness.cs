using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

internal static class WorkflowsDesignJsonIntegrityWitness
{
    internal static async Task RunAsync(WorkflowsDesignProviderFixture fixture, Func<string, WorkflowsDesignDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        const string raw = """{ "OrderID":9007199254740993, "amount":1.00, "nil":null, "text":"\u0061 <+>" }""";
        var serializer = SerializationServices.PayloadSerializer();
        var tenant = "json-tenant-" + Guid.NewGuid().ToString("N");
        var definition = "json-definition-" + Guid.NewGuid().ToString("N");
        var version = "json-version-" + Guid.NewGuid().ToString("N");
        var operation = "json-materialize-" + Guid.NewGuid().ToString("N");
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        ArgumentValue argument;
        using (var source = JsonDocument.Parse(raw)) argument = new(source.RootElement, "Literal");
        var state = new WorkflowDefinitionState(
            [new("variable", "Variable", new TypeReference("Object"), null, ArgumentValue.FromClr(new ScopedDefault { Name = "René <+>" }, "Literal"))],
            new("root", "activity", [new("input", argument, null, null, null, null)], []), [], [], null);
        await using (var context = createContext(fixture.ConnectionString))
        {
            await context.Database.EnsureCreatedAsync();
            context.Definitions.Add(new WorkflowDefinition { Id = definition, TenantId = tenant, Name = "Raw JSON" });
            await context.SaveChangesAsync();
            await new EfMaterializeWorkflowDefinitionVersionCommand(context, access, new EfDesignAtomicWriter(context, access), serializer)
                .Execute(new(operation), new(definition, "1.0.0") { Id = version, TenantId = tenant, State = state });
        }
        await using var fresh = createContext(fixture.ConnectionString);
        var stored = await fresh.Versions.AsNoTracking().SingleAsync(row => row.Id == version);
        using (var wire = JsonDocument.Parse(stored.StateSource!))
            Assert.Equal(raw, wire.RootElement.GetProperty("rootActivity").GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
        var definitions = new EfWorkflowDefinitionStore(fresh, access);
        var loaded = (await new EfWorkflowDefinitionVersionStore(fresh, serializer, definitions, access).FindByIdAsync(version))!.State;
        var value = Assert.Single(loaded.RootActivity!.Inputs).Value.Value!.Value;
        Assert.Equal(raw, value.GetRawText());
        Assert.Equal(9007199254740993L, value.GetProperty("OrderID").GetInt64());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("nil").ValueKind);
        Assert.False(value.TryGetProperty("missing", out _));
        Assert.False(value.TryGetProperty("orderID", out _));
        var scoped = Assert.Single(loaded.Variables).Default!.Value!.Value;
        Assert.Equal("René <+>", scoped.GetProperty("declared_name").GetString());
        Assert.False(scoped.TryGetProperty("name", out _));
        Assert.Equal("René <+>", JsonConversionContract.Deserialize<ScopedDefault>(scoped)!.Name);
        var originalText = stored.StateSource;
        var count = await fresh.Operations.CountAsync();
        using var equivalent = JsonDocument.Parse("""{"text":"a <+>","nil":null,"amount":1e0,"OrderID":9007199254740993}""");
        var retry = state with { RootActivity = state.RootActivity! with { Inputs = [new("input", new(equivalent.RootElement, "Literal"), null, null, null, null)] } };
        await new EfMaterializeWorkflowDefinitionVersionCommand(fresh, access, new EfDesignAtomicWriter(fresh, access), serializer)
            .Execute(new(operation), new(definition, "1.0.0") { Id = version, TenantId = tenant, State = retry });
        Assert.Equal(originalText, (await fresh.Versions.AsNoTracking().SingleAsync(row => row.Id == version)).StateSource);
        Assert.Equal(count, await fresh.Operations.CountAsync());
        var foreign = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant + "-other")));
        Assert.Null(await new EfWorkflowDefinitionVersionStore(fresh, serializer, new EfWorkflowDefinitionStore(fresh, foreign), foreign).FindByIdAsync(version));
    }

    [JsonConversion(MemberMappings = ["declared_name=Name"])]
    public sealed class ScopedDefault { public string Name { get; set; } = string.Empty; }
}
