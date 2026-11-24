using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Acme.Processing.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Acme.Processing.Tests;

public sealed class ProcessingJsonBoundaryTests
{
    [Theory]
    [InlineData("startedAt")]
    [InlineData("completedAt")]
    public async Task Offset_less_API_dates_are_refused_with_the_safe_code_and_owned_member_path(string field)
    {
        await using var host = await Host.Create();
        var body = new Dictionary<string, object?>
        {
            ["customerCategory"] = "René", ["status"] = "complete",
            ["startedAt"] = "2026-11-23T13:30:00+05:45", ["completedAt"] = "2026-11-23T14:30:00+05:45"
        };
        body[field] = "2026-11-23T13:30:00";
        using var response = await host.Client.PostAsJsonAsync("/_acme/processing/records", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("json-datetime-offset-missing", problem.GetProperty("code").GetString());
        Assert.Equal("$." + field, problem.GetProperty("path").GetString());
        Assert.Equal(0, host.Records.Writes);
    }

    [Fact]
    public async Task Explicit_API_offsets_keep_the_instant_and_supplied_offset_at_admission()
    {
        await using var host = await Host.Create();
        using var body = new StringContent("""{"customerCategory":"René","startedAt":"2026-11-23T13:30:00+05:45","completedAt":null,"status":"running"}""", Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync("/_acme/processing/records", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var admitted = Assert.IsType<ProcessingCreate>(host.Records.Last);
        Assert.Equal(TimeSpan.FromMinutes(345), admitted.StartedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 7, 45, 0, TimeSpan.Zero), admitted.StartedAt);
        Assert.Null(admitted.CompletedAt);
        Assert.Equal(1, host.Records.Writes);
    }

    [Theory]
    [InlineData("/_acme/processing/records/record")]
    [InlineData("/_acme/processing/records?take=1")]
    public async Task Both_API_read_routes_emit_explicit_UTC_dates_and_baseline_text(string route)
    {
        await using var host = await Host.Create();
        using var response = await host.Client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("René", text);
        using var document = JsonDocument.Parse(text);
        var row = route.Contains('?') ? document.RootElement.GetProperty("items")[0] : document.RootElement;
        var at = DateTimeOffset.Parse(row.GetProperty("startedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, at.Offset);
        Assert.Equal(Records.Started, at);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("completedAt").ValueKind);
    }

    private sealed class Host(WebApplication app, HttpClient client, Records records) : IAsyncDisposable
    {
        internal HttpClient Client => client;
        internal Records Records => records;
        internal static async Task<Host> Create()
        {
            var records = new Records();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IProcessingRecords>(records);
            var app = builder.Build();
            new ProcessingFeature().MapEndpoints(app, null);
            await app.StartAsync();
            return new(app, app.GetTestClient(), records);
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class Records : IProcessingRecords
    {
        internal static readonly DateTimeOffset Started = new(2026, 11, 23, 13, 30, 0, TimeSpan.FromMinutes(345));
        private static readonly ProcessingRecordView Row = new("record", "René", true, Started, null, "running");
        internal ProcessingCreate? Last;
        internal int Writes;
        public ValueTask<string> CreateAsync(ProcessingCreate input, CancellationToken cancellationToken = default)
        { Last = input; Writes++; return ValueTask.FromResult("record"); }
        public ValueTask<ProcessingRecordView?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<ProcessingRecordView?>(Row);
        public ValueTask<ProcessingRecordViewPage> ReadAsync(int take = 25, string? cursor = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProcessingRecordViewPage([Row], null));
    }
}
