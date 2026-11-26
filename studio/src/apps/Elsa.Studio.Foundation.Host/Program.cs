using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Hosting.Distribution;

// The bare Studio shell: the Studio API, backend profiles, the Theme Store and module administration are compiled in.
// Every other Studio module is a Nuplane add-on installed from the feed.
var builder = WebApplication.CreateBuilder(args);
builder.AddElsaStudioDistribution([typeof(FeatureManagementStudioFeature).Assembly]);

var app = builder.Build();
app.UseElsaStudioDistribution();
app.Run();
