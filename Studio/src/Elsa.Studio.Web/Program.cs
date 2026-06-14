using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;
using Elsa.Studio.Samples.WeatherForecast.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

builder.Services.AddElsaStudioApi();
builder.Services.AddDashboardStudioSample();
builder.Services.AddWeatherForecastStudioSample();

var app = builder.Build();

app.UseStaticFiles();

app.MapElsaStudioApi();
app.MapWeatherForecastStudioSample();
app.MapFallbackToFile("studio/index.html");

app.Run();

