using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// One attempt's view of an ordered reusable-activity publication: the three module contexts it commits through,
/// each owning its own connection, and the EF stores and commands composed over them.
/// </summary>
internal sealed class ActivityPublicationScope : IAsyncDisposable
{
    public ActivityPublicationScope(
        PublishingSnapshotReviewDbContext publishing,
        ActivitiesDesignDbContext design,
        RuntimeDbContext runtime,
        TestAccess access)
    {
        Publishing = publishing;
        Design = design;
        Runtime = runtime;
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = "publishing-ef-test-recovery-signing-key-32-bytes",
            AllowEphemeralDevelopmentKey = false
        }));
        DesignStores = new EfActivityDesignStores(design, access, null, new EfActivityManagementProjectionWriter(design, access));
        Templates = new EfExecutableActivityTemplateStore(runtime, access, codec);
        SourceReferences = new EfWorkflowExecutableSourceReferenceStore(runtime, access, codec);
        Receipts = new EfActivityPublicationReceiptStore(publishing, access);
        Command = new EfActivityPublicationCommand(Receipts, Templates, SourceReferences, DesignStores, access);
        SourceCommand = new EfSourceActivityPublicationCommand(Templates, SourceReferences, DesignStores, access);
    }

    public PublishingSnapshotReviewDbContext Publishing { get; }
    public ActivitiesDesignDbContext Design { get; }
    public RuntimeDbContext Runtime { get; }
    public EfActivityDesignStores DesignStores { get; }
    public EfExecutableActivityTemplateStore Templates { get; }
    public EfWorkflowExecutableSourceReferenceStore SourceReferences { get; }
    public EfActivityPublicationReceiptStore Receipts { get; }
    public EfActivityPublicationCommand Command { get; }
    public EfSourceActivityPublicationCommand SourceCommand { get; }

    public async ValueTask DisposeAsync()
    {
        await Publishing.DisposeAsync();
        await Design.DisposeAsync();
        await Runtime.DisposeAsync();
    }
}
