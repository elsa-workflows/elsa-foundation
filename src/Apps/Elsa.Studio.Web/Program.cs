using Elsa.Admin.Api;
using Elsa.Admin.Api.Extensions;
using Elsa.Admin.Samples.Dashboard;
using Elsa.Admin.Samples.WeatherForecast;
using Elsa.Admin.Samples.WeatherForecast.Extensions;
using Elsa.Admin.Web;
using Elsa.Admin.Web.Extensions;
using Elsa.Events;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

new EventsFeature().ConfigureServices(builder.Services);
new AdminApiFeature().ConfigureServices(builder.Services);
new AdminWebFeature().ConfigureServices(builder.Services);
new DashboardAdminSampleFeature().ConfigureServices(builder.Services);
new WeatherForecastAdminSampleFeature().ConfigureServices(builder.Services);

var app = builder.Build();

app.UseStaticFiles();

app.MapElsaAdminApi();
app.MapWeatherForecastAdminSample();
app.MapElsaAdminWeb();

app.Run();
