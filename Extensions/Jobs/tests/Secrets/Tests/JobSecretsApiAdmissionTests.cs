using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Jobs.Api;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Jobs.Secrets.Tests;

public sealed class JobSecretsApiAdmissionTests
{
    [Fact]
    public async Task Actual_http_submit_and_schedule_refuse_invalid_reference_slots_before_the_authority_and_require_no_secret_store()
    {
        await using var authority = await SecretJobHost.StartAsync(installSecrets: false);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().SetApplicationName("Jobs.SecretAdmission.Tests").PersistKeysToFileSystem(new(Path.Combine(authority.StorageDirectory, "api-keys")));
        builder.Services.AddAuthentication(AdmissionAuthentication.SchemeId).AddScheme<AuthenticationSchemeOptions, AdmissionAuthentication>(AdmissionAuthentication.SchemeId, _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string> { AdmissionAuthentication.SchemeId });
        builder.Services.ReplaceCurrentSubjectPermissionAuthorizer<Granted>();
        builder.Services.AddJobsEntityFrameworkCore(options => options.ConnectionString = "Data Source=" + Path.Combine(authority.StorageDirectory, "jobs.db") + ";Pooling=False");
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
        SecretJobHost.AddDeclarations(builder.Services); builder.Services.AddJobsApi(options => options.PersistencePartition = SecretJobHost.Partition);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); JobsApi.MapJobsApi(app); await app.StartAsync();
        using var client = app.GetTestClient();
        var invalid = SecretJobHost.Request(new { credential = "PLAIN_HTTP_CREDENTIAL_CANARY", @protected = (string?)null, mode = "safe" });
        object Invocation(JobRequest request) => new { request.Handler, request.Input, request.ResultSchema, request.Queue, request.Policy, request.NotBefore };
        var submission = await client.PostAsJsonAsync("/jobs", new { idempotencyKey = "bad-input", invocation = Invocation(invalid) });
        Assert.Equal(HttpStatusCode.BadRequest, submission.StatusCode); Assert.DoesNotContain("CANARY", await submission.Content.ReadAsStringAsync());
        var schedule = await client.PutAsJsonAsync("/jobs/schedules/hourly", new { invocation = Invocation(invalid), recurrence = new JobRecurrence(JobRecurrenceKind.Interval, "PT1H"),
            firstOccurrence = DateTimeOffset.UtcNow.AddHours(1), missedPolicy = JobMissedOccurrencePolicy.CoalesceOne, expectedRevision = 0, operationId = "bad-schedule", reason = "Test reference admission" });
        Assert.Equal(HttpStatusCode.BadRequest, schedule.StatusCode);
        var empty = await authority.UseAsync(async services => await services.GetRequiredService<IJobQueries>().ListJobsAsync(new(SecretJobHost.Partition, SecretJobHost.Alpha), new(new(25, null), null, null, null)));
        Assert.Empty(empty.Items);
        Assert.Null(await authority.UseAsync(async services => await services.GetRequiredService<IJobQueries>().FindScheduleAsync(new(SecretJobHost.Partition, SecretJobHost.Alpha), "hourly")));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/jobs", new { idempotencyKey = "valid-reference", invocation = Invocation(SecretJobHost.Request()) })).StatusCode);
    }
    private sealed class Granted : ICurrentSubjectPermissionAuthorizer
    {
        public Granted() { }
        public ValueTask<CurrentSubjectPermissionResult> AuthorizeAsync(CurrentSubjectPermissionRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(CurrentSubjectPermissionResult.Granted);
    }
    private sealed class AdmissionAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        internal const string SchemeId = "JobsSecretAdmission";
        public AdmissionAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(
            [new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.TenantId, SecretJobHost.Alpha), new(IdentityClaimTypes.UserId, "operator"), new(IdentityClaimTypes.Permission, "*")], SchemeId)), SchemeId)));
    }
}
