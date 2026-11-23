using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDocumentDraftSaveTests
{
    private const string Xml = """
        <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="urn:test">
          <b:process id="P" isExecutable="true"><b:startEvent id="Start"/><b:serviceTask id="Work" name="Before"/><b:complexGateway id="Advanced"/>
            <b:sequenceFlow id="Flow" sourceRef="Start" targetRef="Work"/>
          </b:process>
        </b:definitions>
        """;

    [Fact]
    public async Task Actual_inline_validated_create_and_update_commit_complete_unsupported_source_and_emit_diagnostics()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new EventsFeature().ConfigureServices(services);
        services.AddEventHandler<DraftValidating, ExecuteValidations>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inline = scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>();
        var structures = new DefaultActivityStructureService(scope.ServiceProvider.GetServices<IActivityStructureHandler>());
        var serializer = SerializationServices.PayloadSerializer();
        var deferred = new CapturedEvents();
        var accessor = new Accessor();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WorkflowsDesignSqliteDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db, accessor);
        await new EfMaterializeWorkflowDefinitionCommand(db, accessor, writer).Execute(new DesignOperationKey("complete-source-definition"),
            new WorkflowDefinition { Id = "definition", Name = "Complete document" });
        var native = new ActivityNode("native", "exact-native-version", [], [], new("vendor.native", "7",
            JsonSerializer.SerializeToElement(new { privateSetting = "retained-native-configuration", nested = new { keep = true } })));
        var document = new BpmnAuthoredDocument(Xml, "P", [native], [new("Work", native.NodeId)], [])
        { AdditionalProperties = new() { ["futureMetadata"] = JsonSerializer.SerializeToElement(new { keep = true }) } };
        var owner = new ActivityNode("owner", "exact-process-version", [], [], new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(document, BpmnDocumentStructureHandler.SerializerOptions)));
        var state = new WorkflowDefinitionState([], owner, [], [], null);
        var locks = new Locks();
        var id = await new EfCreateDraftCommand(db, accessor, writer, new Identities(), serializer, locks, inline, deferred)
            .Execute(new DesignOperationKey("complete-source-create"), "definition", state);
        var created = Assert.Single(deferred.Events.OfType<DraftValidated>());
        Assert.Contains(created.Errors, error => error.Type == "Bpmn/ExecutionCapability" && error.Message.Contains("not qualified", StringComparison.Ordinal));
        Assert.Equal(Xml, BpmnDocumentStructureHandler.Read(created.Draft.State.RootActivity!).DocumentXml);

        var updatedXml = Xml.Replace("name=\"Before\"", "name=\"After\"", StringComparison.Ordinal);
        var updatedOwner = BpmnDocumentScopeProjection.Write(owner, document with { DocumentXml = updatedXml });
        await new EfUpdateDraftCommand(db, accessor, writer, serializer, structures, locks, inline, deferred)
            .Execute(new DesignOperationKey("complete-source-update"), new UpdateDraftRequest(id, state with { RootActivity = updatedOwner }, []));
        var updatedEvent = deferred.Events.OfType<DraftValidated>().Last();
        Assert.Equal(2, deferred.Events.OfType<DraftValidated>().Count());
        Assert.Contains(updatedEvent.Errors, error => error.Type == "Bpmn/ExecutionCapability");

        db.ChangeTracker.Clear();
        var persisted = await db.Drafts.SingleAsync(row => row.Id == id);
        var persistedState = serializer.Deserialize<WorkflowDefinitionState>(persisted.StateSource!);
        var retained = BpmnDocumentStructureHandler.Read(persistedState.RootActivity!);
        Assert.Equal(updatedXml, retained.DocumentXml);
        Assert.Equal("exact-native-version", Assert.Single(retained.Activities).ActivityVersionId);
        Assert.True(JsonElement.DeepEquals(native.Structure!.Payload, retained.Activities.Single().Structure!.Payload));
        Assert.True(JsonElement.DeepEquals(document.AdditionalProperties!["futureMetadata"], retained.AdditionalProperties!["futureMetadata"]));
        Assert.Contains("After", BpmnDocumentStructureHandler.Read(updatedEvent.Draft.State.RootActivity!).DocumentXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_op_save_keeps_independent_process_headers_and_native_descendant_presentation_but_prunes_deleted_ownership()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var serializer = SerializationServices.PayloadSerializer();
        var accessor = new Accessor();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WorkflowsDesignSqliteDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db, accessor);
        const string otherProcess = "<b:process id='P2'><b:subProcess id='Scope'><b:task id='Hidden'/></b:subProcess></b:process>";
        var xml = $"<b:definitions xmlns:b='{BpmnDocumentStructureHandler.ModelNamespace}'><b:process id='P1'><b:task id='Visible'/></b:process>" +
            otherProcess + "</b:definitions>";
        var visible = new ActivityNode("visible", "native@1", [], []);
        var leaf = new ActivityNode("hidden-leaf", "native@1", [], []);
        var sequence = new ActivityNode("hidden-sequence", "sequence@1", [], [], new("elsa.sequence.structure", "1.0.0",
            JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([leaf], []), BpmnDocumentStructureHandler.SerializerOptions)));
        var document = new BpmnAuthoredDocument(xml, "P1", [visible, sequence], [new("Visible", visible.NodeId), new("Hidden", sequence.NodeId)], [])
        { ScopeHeaders = [new("P2", "p2-header", "process@1", [], [], []), new("Scope", "scope-header", "process@1", [], [], [])] };
        var owner = new ActivityNode("owner", "process@1", [], [], new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(document, BpmnDocumentStructureHandler.SerializerOptions)));
        Assert.Equal("visible", Assert.Single(structures.ProjectChildren(owner).Single().Activities).NodeId);
        var state = new WorkflowDefinitionState([], owner, [], [], null);
        var presentation = new[] { "owner", "visible", "p2-header", "scope-header", "hidden-sequence", "hidden-leaf", "ghost" }
            .Select(id => new ActivityPresentationRecord(id, $"Name {id}", $"Description {id}")).ToArray();
        var definition = new WorkflowDefinition { Id = "presentation-definition", Name = "Presentation retention" };
        var draft = new WorkflowDefinitionDraft { Id = "presentation-draft", WorkflowDefinitionId = definition.Id, State = state };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, new Identities())
            .Execute(new DesignOperationKey("presentation-seed"), definition, draft, [], presentation);
        var command = new EfUpdateDraftCommand(db, accessor, writer, serializer, structures, new Locks());
        await command.Execute(new DesignOperationKey("presentation-no-op"), new UpdateDraftRequest(draft.Id, state, [], presentation));
        db.ChangeTracker.Clear();
        var stored = await new EfWorkflowDefinitionDraftStore(db, serializer, accessor).FindWithLayoutByIdAsync(draft.Id);
        Assert.Equal(presentation.Where(record => record.NodeId != "ghost").OrderBy(record => record.NodeId),
            stored!.ActivityPresentation.OrderBy(record => record.NodeId));
        Assert.Equal(xml, BpmnDocumentStructureHandler.Read(stored.Draft.State.RootActivity!).DocumentXml);

        var removed = BpmnDocumentScopeProjection.Write(owner, document with
        {
            DocumentXml = xml.Replace(otherProcess, "", StringComparison.Ordinal), Activities = [visible],
            Bindings = [new("Visible", visible.NodeId)], ScopeHeaders = []
        });
        await command.Execute(new DesignOperationKey("presentation-remove-process"),
            new UpdateDraftRequest(draft.Id, state with { RootActivity = removed }, [], presentation));
        db.ChangeTracker.Clear();
        stored = await new EfWorkflowDefinitionDraftStore(db, serializer, accessor).FindWithLayoutByIdAsync(draft.Id);
        Assert.Equal(presentation.Where(record => record.NodeId is "owner" or "visible").OrderBy(record => record.NodeId),
            stored!.ActivityPresentation.OrderBy(record => record.NodeId));
        Assert.Equal(xml, document.DocumentXml);
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope("complete-source-test"));
    }
    private sealed class Identities : IIdentityGenerator { private int _next; public string Generate() => "draft-" + Interlocked.Increment(ref _next); }
    private sealed class CapturedEvents : IDeferredEventPublisher
    {
        internal List<IEvent> Events { get; } = [];
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) { Events.Add(@event); return Task.CompletedTask; }
    }
    private sealed class Locks : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        private sealed class Handle : IDistributedSynchronizationHandle
        { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
