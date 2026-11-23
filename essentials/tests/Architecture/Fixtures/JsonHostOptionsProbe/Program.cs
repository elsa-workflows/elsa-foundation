using Microsoft.AspNetCore.Builder;

// Negative control: default ASP.NET HTTP options must fail the production baseline probe.
var app = WebApplication.CreateBuilder(args).Build();
app.Run();
