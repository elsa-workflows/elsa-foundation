using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Json.Options;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation.Json;

[ShellFeature(
    name: "ActivitiesDesignReconciliationJson",
    DisplayName = "JSON activity reconciliation source",
    Description = "Reconciliation source that reads activity definitions from a JSON file."
)]
public class ActivitiesDesignReconciliationJsonFeature : IShellFeature
{
    public JsonReconciliationOptions Options { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddScoped<JsonActivityCatalogReader>();        
    }
}
