using Elsa.Api.FastEndpoints.Constants;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityImportHttpContractTests
{
    [Fact]
    public void Routes_are_exact_and_permission_guarded()
    {
        var service = new ThrowingService();
        Action<DefaultHttpContext> context = _ => { };
        var upload = Factory.Create<UploadReusableActivityCollectionEndpoint>(context, service);
        var analyze = Factory.Create<AnalyzeReusableActivityCollectionEndpoint>(
            context,
            service,
            Options.Create(new ReusableActivityImportOptions()));
        var selection = Factory.Create<ExpandReusableActivityImportSelectionEndpoint>(context, service);
        var apply = Factory.Create<ApplyReusableActivityImportEndpoint>(context, service);
        var status = Factory.Create<GetReusableActivityImportStatusEndpoint>(context, service);

        upload.Configure();
        analyze.Configure();
        selection.Configure();
        apply.Configure();
        status.Configure();

        AssertEndpoint(upload.Definition, "POST", "migration/elsa3/reusable-activities/collections", PermissionNames.Elsa3ImportManage);
        AssertEndpoint(analyze.Definition, "GET", "migration/elsa3/reusable-activities/collections/{collectionHandle}/analysis", PermissionNames.Elsa3ImportRead);
        AssertEndpoint(selection.Definition, "POST", "migration/elsa3/reusable-activities/collections/{collectionHandle}/selection", PermissionNames.Elsa3ImportRead);
        AssertEndpoint(apply.Definition, "POST", "migration/elsa3/reusable-activities/collections/{collectionHandle}/apply", PermissionNames.Elsa3ImportManage);
        AssertEndpoint(status.Definition, "GET", "migration/elsa3/reusable-activities/imports/{idempotencyKey}", PermissionNames.Elsa3ImportRead);
    }

    private static void AssertEndpoint(
        EndpointDefinition definition,
        string verb,
        string route,
        string permission)
    {
        Assert.Equal(verb, Assert.Single(definition.Verbs));
        Assert.Equal(route, Assert.Single(definition.Routes));
        Assert.Contains(permission, definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    private sealed class ThrowingService : IReusableActivityImportOperationService
    {
        public ValueTask<ReusableActivityImportUploadResult> UploadAsync(Stream json, long? contentLength, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReusableActivityImportAnalysisPage> AnalyzeAsync(string collectionHandle, int offset, int limit, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReusableActivityImportSelectionReadiness> ExpandSelectionAsync(string collectionHandle, string planId, IReadOnlyCollection<string> selectedSourceVersionIds, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReusableActivityImportReceipt> ApplyAsync(string collectionHandle, string planId, IReadOnlyCollection<string> selectedSourceVersionIds, string idempotencyKey, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReusableActivityImportReceipt> GetStatusAsync(string idempotencyKey, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
