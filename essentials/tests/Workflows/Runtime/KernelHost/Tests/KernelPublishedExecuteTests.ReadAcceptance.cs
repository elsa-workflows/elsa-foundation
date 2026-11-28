using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Services;
using Xunit;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed partial class KernelPublishedExecuteTests
{
    private static async Task VerifyExecutableRetentionAsync(HttpClient http, string artifactId, string sourceId, int count,
        HttpStatusCode sourceStatus, CancellationToken token)
    {
        var executable = $"/runtime/workflows/executables/{Uri.EscapeDataString(artifactId)}";
        var inputs = $"{executable}/source-references/{Uri.EscapeDataString(sourceId)}/input-sources";
        using var provenance = await ReadAsync(executable + "/provenance");
        Assert.Equal(HttpStatusCode.OK, provenance.StatusCode);
        var retained = Assert.IsType<ExecutableProvenanceView>(await provenance.Content.ReadFromJsonAsync<ExecutableProvenanceView>(token));
        Assert.Equal(count, retained.RetainedExecutionCount);

        using var selected = await ReadAsync(executable + "?ref=" + Uri.EscapeDataString(sourceId));
        Assert.Equal(sourceStatus, selected.StatusCode);
        if (sourceStatus == HttpStatusCode.OK)
        {
            var detail = Assert.IsType<WorkflowExecutableDetailsView>(await selected.Content.ReadFromJsonAsync<WorkflowExecutableDetailsView>(token));
            Assert.Equal(sourceId, detail.ChosenReference?.SourceReferenceId);
            Assert.Equal("requested", detail.ChosenReference?.Selection);
        }
        using var forbidden = await ReadAsync(inputs);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var authored = await ReadAsync(inputs, publishing: true);
        Assert.Equal(sourceStatus, authored.StatusCode);

        async Task<HttpResponseMessage> ReadAsync(string url, bool publishing = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Fixture-Subject", "accepted");
            request.Headers.Add("X-Fixture-Tenant", Scope.Value);
            request.Headers.Add("X-Fixture-Read", "yes");
            if (publishing) request.Headers.Add("X-Fixture-Publishing-Read", "yes");
            return await http.SendAsync(request, token);
        }
    }

    private sealed record ProjectedRun(Contract.IActorReads Reads, Contract.BoundScope Scope, Contract.ActorRef Actor,
        string Definition, Contract.History History)
    {
        internal async Task<Contract.ActorView> ReadAsync(Contract.ActorStatus status, bool parked, CancellationToken token)
        {
            Contract.ActorView? point;
            while ((point = await Reads.GetAsync(Scope, Actor, token)) is null ||
                point.Header.Status != status || (point.Park is not null) != parked)
                await Task.Delay(40, token);

            var filter = new Contract.ActorQuery(Type: Kernel.WorkflowActor.WorkflowActor.ActorType, Status: status,
                Labels: new Dictionary<string, string> { ["definition"] = Definition }, Parked: parked);
            var page = await Reads.ListAsync(Scope, filter, cancellationToken: token);
            var listed = Assert.Single(page.Items, view => view.Header.Ref == Actor);
            Assert.Equal(Scope.Tenant, listed.Header.Tenant);
            Assert.Equal(status, listed.Header.Status);
            Assert.Equal(Definition, listed.Header.Labels["definition"]);
            Assert.Equal(History, Kernel.WorkflowActor.WorkflowActor.DescribeRun(listed.Header)?.History);
            Assert.Equal(point.Park, listed.Park);
            Assert.Equal(point.Header.EndedAt, listed.Header.EndedAt);
            if (status == Contract.ActorStatus.Finished)
                Assert.True(listed.Header.EndedAt is > 0);
            else
                Assert.Null(listed.Header.EndedAt);
            if (parked || status == Contract.ActorStatus.Finished)
                Assert.NotEmpty(listed.Selection!);
            return listed;
        }
    }

    private static async Task VerifyTenantReadIsolationAsync(HttpClient http, Contract.IActorReads reads, Contract.BoundScope owner,
        Contract.ActorRef actor, string definition, CancellationToken token)
    {
        const string instances = "/runtime/workflows/instances";
        var foreign = ContractScopes.Bind("another-tenant");
        var missing = new Contract.ActorRef(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Assert.Null(await reads.GetAsync(owner, missing, token));
        Assert.NotEmpty((await reads.InboxAsync(owner, actor, cancellationToken: token)).Items);

        foreach (var reference in new[] { actor, missing })
        {
            Assert.Null(await reads.GetAsync(foreign, reference, token));
            var journal = await reads.TailAsync(foreign, reference, cancellationToken: token);
            Assert.Empty(journal.Items);
            Assert.Null(journal.Next);
            Assert.Null(journal.JournalHeadRevision);
            Assert.Null(journal.JournalCatchUpToken);
            Assert.Null(journal.JournalCancellation);
            var inbox = await reads.InboxAsync(foreign, reference, cancellationToken: token);
            Assert.Empty(inbox.Items);
            Assert.Null(inbox.Next);
        }

        using var ownList = await ReadAsync(ListUrl(actor), owner.Tenant);
        Assert.Equal(HttpStatusCode.OK, ownList.StatusCode);
        var visible = Assert.IsType<WorkflowInstanceListView>(await ownList.Content.ReadFromJsonAsync<WorkflowInstanceListView>(token));
        Assert.Equal(actor.Id, Assert.Single(visible.Items).WorkflowExecutionId);
        using var foreignList = await ReadAsync(ListUrl(actor), foreign.Tenant);
        using var missingList = await ReadAsync(ListUrl(missing), foreign.Tenant);
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.Equal(foreignList.StatusCode, missingList.StatusCode);
        Assert.Equal(await missingList.Content.ReadAsStringAsync(token), await foreignList.Content.ReadAsStringAsync(token));
        var empty = Assert.IsType<WorkflowInstanceListView>(await foreignList.Content.ReadFromJsonAsync<WorkflowInstanceListView>(token));
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);

        foreach (var endpoint in new[] { "", "/history", "/history/live", "/history/occurrences", "/park" })
        {
            using var ownRead = await ReadAsync($"{instances}/{actor.Id}{endpoint}", owner.Tenant);
            Assert.True(ownRead.StatusCode == HttpStatusCode.OK, $"The owner could not read '{endpoint}' ({ownRead.StatusCode}).");
            if (endpoint == "/history/live")
                Assert.Equal("application/x-ndjson", ownRead.Content.Headers.ContentType?.MediaType);

            using var foreignRead = await ReadAsync($"{instances}/{actor.Id}{endpoint}", foreign.Tenant);
            using var missingRead = await ReadAsync($"{instances}/{missing.Id}{endpoint}", foreign.Tenant);
            Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
            Assert.Equal(foreignRead.StatusCode, missingRead.StatusCode);
            Assert.Equal(await missingRead.Content.ReadAsStringAsync(token), await foreignRead.Content.ReadAsStringAsync(token));
        }

        string ListUrl(Contract.ActorRef reference) =>
            $"{instances}/page?definitionId={Uri.EscapeDataString(definition)}&workflowExecutionId={reference.Id}";

        async Task<HttpResponseMessage> ReadAsync(string url, string tenant)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Fixture-Subject", FixtureReadAuthorizer.Subject);
            request.Headers.Add("X-Fixture-Tenant", tenant);
            request.Headers.Add("X-Fixture-Read", "yes");
            request.Headers.Add("X-Fixture-Inspect", "yes");
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
    }

    private sealed class FixtureReadAuthorizer : ICurrentSubjectPermissionAuthorizer
    {
        internal const string Subject = "read-verifier";

        public ValueTask<CurrentSubjectPermissionResult> AuthorizeAsync(CurrentSubjectPermissionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(request is { SubjectId: Subject, Permission: WorkflowRuntimePermissions.WorkflowRuntimeRead or
                HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission }
                ? CurrentSubjectPermissionResult.Granted
                : CurrentSubjectPermissionResult.Denied("The fixture subject has no current read grant."));
    }
}
