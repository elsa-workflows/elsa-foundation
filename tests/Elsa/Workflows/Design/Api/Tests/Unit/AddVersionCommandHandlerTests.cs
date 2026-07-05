using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Covers issue #404: <see cref="AddVersionCommandHandler"/> must follow the same
/// locking/existence-check discipline as the promote/reconcile paths. Without it, two concurrent
/// <c>POST /versions</c> calls both read the same latest version, both compute e.g. "2.0.0", and on
/// the Groundwork backend (no unique constraint) both persist silently.
/// </summary>
public sealed class AddVersionCommandHandlerTests
{
    private readonly List<string> _events = [];
    private readonly RecordingLockProvider _lockProvider;
    private readonly StubDefinitionStore _definitionStore = new();
    private readonly RecordingAddCommand _addCommand = new();

    public AddVersionCommandHandlerTests()
    {
        _lockProvider = new();
    }

    private AddVersionCommandHandler CreateHandler(StubVersionStore versionStore) =>
        new(new StubVersionFactory(), _addCommand, versionStore, _definitionStore, _lockProvider);

    [Fact]
    public async Task Race_computed_duplicate_version_is_rejected_not_silently_persisted()
    {
        // Deterministic replay of the filed race: by the time this handler computed "2.0.0", a
        // concurrent publish already persisted "2.0.0" - the store reports it as existing.
        var versionStore = new StubVersionStore(_events)
        {
            Latest = new WorkflowDefinitionVersion("def-1", "1.0.0") { Id = "v1" },
            ExistingSortKeys = { SemVer.ToSortKey("2.0.0") },
        };
        var handler = CreateHandler(versionStore);

        await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(
            () => handler.Handle(new AddVersion("def-1", new WorkflowDefinitionStateView()), CancellationToken.None));

        Assert.Empty(_addCommand.Added);
    }

    [Fact]
    public async Task Publish_waits_for_the_definition_lock_before_reading_the_latest_version()
    {
        var versionStore = new StubVersionStore(_events)
        {
            Latest = new WorkflowDefinitionVersion("def-1", "1.0.0") { Id = "v1" },
        };
        var handler = CreateHandler(versionStore);

        // Hold the definition lock, start the publish, then release. A handler that takes the lock
        // reads the latest version only after "released"; the unfixed handler reads it immediately.
        var handle = await _lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DefinitionKey("def-1"));
        var publish = handler.Handle(new AddVersion("def-1", new WorkflowDefinitionStateView()), CancellationToken.None);

        _events.Add("released");
        await handle.DisposeAsync();
        await publish;

        Assert.Equal(new[] { "released", "find-latest" }, _events.Where(e => e is "released" or "find-latest"));
        Assert.Equal("workflow-definition:def-1", _lockProvider.LastLockName);
    }

    [Fact]
    public async Task Publishes_the_next_major_version()
    {
        var versionStore = new StubVersionStore(_events)
        {
            Latest = new WorkflowDefinitionVersion("def-1", "2.3.1") { Id = "v1" },
        };
        var handler = CreateHandler(versionStore);

        var result = await handler.Handle(new AddVersion("def-1", new WorkflowDefinitionStateView()), CancellationToken.None);

        var added = Assert.Single(_addCommand.Added);
        Assert.Equal("3.0.0", added.Version);
        Assert.Equal("3.0.0", result.Version);
    }

    [Fact]
    public async Task First_version_is_one_dot_zero()
    {
        var handler = CreateHandler(new StubVersionStore(_events));

        await handler.Handle(new AddVersion("def-1", new WorkflowDefinitionStateView()), CancellationToken.None);

        Assert.Equal("1.0.0", Assert.Single(_addCommand.Added).Version);
    }

    private static WorkflowDefinitionState EmptyState() => new WorkflowDefinitionStateView().ToState();

    private sealed class StubDefinitionStore : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowDefinition { Id = id, Name = $"definition {id}" });

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinition?>(new WorkflowDefinition { Id = id, Name = $"definition {id}" });

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);
    }

    private sealed class StubVersionStore(List<string> events) : IWorkflowDefinitionVersionStore
    {
        public WorkflowDefinitionVersion? Latest { get; init; }
        public HashSet<string> ExistingSortKeys { get; } = [];

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
        {
            // The stub factory generates ids as "v-{version}"; mirror the added version back.
            var version = new WorkflowDefinitionVersion("def-1", versionId.StartsWith("v-") ? versionId[2..] : "1.0.0")
            {
                Id = versionId,
                State = EmptyState(),
                Definition = new WorkflowDefinition { Id = "def-1", Name = "definition def-1" },
            };
            return Task.FromResult(version);
        }

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            events.Add("find-latest");
            return Task.FromResult(Latest);
        }

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingSortKeys.Contains(semVerSortKey));
    }

    private sealed class RecordingAddCommand : IAddCommand<WorkflowDefinitionVersion>
    {
        public List<WorkflowDefinitionVersion> Added { get; } = [];

        public Task Add(WorkflowDefinitionVersion entity, CancellationToken cancellationToken = default)
        {
            Added.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class StubVersionFactory : IWorkflowDefinitionVersionFactory
    {
        public IWorkflowDefinitionVersion Create(
            IWorkflowDefinition definition,
            string version,
            WorkflowDefinitionState state,
            DateTimeOffset? sourceCreatedAt = null,
            string? id = null) =>
            new FakeVersion(id ?? $"v-{version}", version, definition.Id, state);
    }

    private sealed class FakeVersion(string id, string version, string definitionId, WorkflowDefinitionState state) : IWorkflowDefinitionVersion
    {
        public string Id => id;
        public string Version => version;
        public string DefinitionId => definitionId;
        public IWorkflowDefinition Definition => throw new NotSupportedException();
        public WorkflowDefinitionState State => state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset? SourceCreatedAt => null;
    }

    /// <summary>
    /// Single-process lock provider over one semaphore per name; records the last requested lock name
    /// for assertions. Event ordering is observed via the stores, which log into the shared list.
    /// </summary>
    private sealed class RecordingLockProvider : IDistributedLockProvider
    {
        private readonly Dictionary<string, SemaphoreSlim> _gates = [];

        public string? LastLockName { get; private set; }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            LastLockName = name;
            SemaphoreSlim gate;
            lock (_gates)
            {
                if (!_gates.TryGetValue(name, out gate!))
                    _gates[name] = gate = new(1, 1);
            }

            await gate.WaitAsync(cancellationToken);
            return new Handle(gate);
        }

        private sealed class Handle(SemaphoreSlim gate) : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() => gate.Release();

            public ValueTask DisposeAsync()
            {
                gate.Release();
                return default;
            }
        }
    }
}
