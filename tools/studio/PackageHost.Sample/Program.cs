using CShells.DependencyInjection;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddElsaStudio()
    .ConfigureShells(shells => shells.WithAssemblies(typeof(DashboardStudioFeature).Assembly));

var app = builder.Build();
app.UseElsaStudio();
app.MapElsaStudio();
app.Run();
