using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Preflight and publish decide a reusable activity publication through the same context resolution and the same
/// candidate evaluation, so for one draft state they reach one outcome: the same rejection code carrying the same
/// diagnostics, or both accept.
/// </summary>
/// <remarks>
/// The failure this guards against looks like success from the preflight side: a green preflight whose publish is
/// refused, which is what two separately written copies of the pipeline produce once their rules drift. The
/// publishable candidate pins the other direction, so parity that holds only because both sides refuse everything
/// cannot pass, and it proves exactly one commit follows an accepted review.
/// </remarks>
public sealed class ActivityPublicationPreflightParityTests
{
    public enum Candidate
    {
        Publishable,
        StaleLayout,
        VersionTaken,
        VersionBelowRequiredBump,
        FirstVersionBelowOne,
        ValidationErrors,
        ValidationRefusedWithoutErrors,
        CompilationFailed,
        RuntimeConsumerMissing
    }

    [Theory]
    [InlineData(Candidate.Publishable, null)]
    [InlineData(Candidate.StaleLayout, "activity.draft.stale-layout")]
    [InlineData(Candidate.VersionTaken, ActivityErrorCodes.VersionConflict)]
    [InlineData(Candidate.VersionBelowRequiredBump, ActivityErrorCodes.PublicationInvalid)]
    [InlineData(Candidate.FirstVersionBelowOne, ActivityErrorCodes.PublicationInvalid)]
    [InlineData(Candidate.ValidationErrors, ActivityErrorCodes.PublicationInvalid)]
    [InlineData(Candidate.ValidationRefusedWithoutErrors, ActivityErrorCodes.PublicationInvalid)]
    [InlineData(Candidate.CompilationFailed, ActivityErrorCodes.PublicationInvalid)]
    [InlineData(Candidate.RuntimeConsumerMissing, ActivityErrorCodes.PublicationInvalid)]
    public async Task Preflight_and_publish_reach_the_same_outcome_for_the_same_candidate(Candidate candidate, string? expectedErrorCode)
    {
        var scenario = new Scenario(candidate);

        var preflight = await scenario.PreflightAsync();
        var publish = await scenario.PublishAsync();

        Assert.Equal(expectedErrorCode, preflight.ErrorCode);
        Assert.Equal(preflight, publish);
        Assert.Equal(expectedErrorCode is null ? 1 : 0, scenario.Commits.Count);
    }

    /// <summary>
    /// What an entry point concluded, reduced to what a caller can act on. A preflight that returns a view it does not
    /// call publishable is the same refusal publish raises as <see cref="ActivityErrorCodes.PublicationInvalid"/>.
    /// </summary>
    private sealed record Outcome(string? ErrorCode, string DiagnosticCodes)
    {
        public static readonly Outcome Accepted = new(null, "");

        public static Outcome Refused(string errorCode, IEnumerable<ActivityDiagnostic> diagnostics) =>
            new(errorCode, string.Join(",", diagnostics.Select(diagnostic => diagnostic.Code)));
    }

    private sealed class Scenario
    {
        private const string DraftId = "draft-1";
        private const long Revision = 4;
        private const string Consumer = "test.consumer";

        private readonly ActivityDefinitionPublisher _publisher;
        private readonly string? _headVersionId;
        private readonly string _version;

        public Scenario(Candidate candidate)
        {
            var hasHead = candidate is Candidate.VersionTaken or Candidate.VersionBelowRequiredBump;
            _headVersionId = hasHead ? "version-head" : null;
            _version = candidate switch
            {
                Candidate.VersionTaken => "1.0.1",
                Candidate.VersionBelowRequiredBump => "1.1.0",
                Candidate.FirstVersionBelowOne => "0.9.0",
                _ => "1.0.0"
            };

            var definition = new ActivityDefinition { Id = "definition-1", ActivityTypeKey = "test.activity", Category = "Test" };
            var draft = new ActivityDefinitionDraft
            {
                Id = DraftId,
                DefinitionId = definition.Id,
                Revision = Revision,
                Status = ActivityDefinitionDraftStatus.Active,
                State = new(new("1", [], [], []), Provider(), new Dictionary<string, string>())
            };
            var authoring = new ActivityDefinitionAuthoringState
            {
                Id = definition.Id,
                DefinitionId = definition.Id,
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
                HeadVersionId = _headVersionId
            };
            var layout = new ActivityDefinitionDraftLayout
            {
                Id = "layout-draft-1",
                DraftId = DraftId,
                Revision = candidate == Candidate.StaleLayout ? Revision - 1 : Revision,
                Records = [new("boundary", JsonSerializer.SerializeToElement(new { x = 10, y = 20 }))]
            };
            IReadOnlyList<ActivityDefinitionVersionPublication> publications = hasHead
                ? [Publication("version-head", "1.0.0"), Publication("version-later", "1.0.1")]
                : [];
            var validation = candidate switch
            {
                Candidate.ValidationErrors => new ValidationVerdict(false, Diagnostic("activity.contract.schema-required", ActivityDiagnosticSeverity.Error)),
                // A validator may refuse a draft without an error-severity diagnostic; the contract only promises IsValid.
                Candidate.ValidationRefusedWithoutErrors => new ValidationVerdict(false, Diagnostic("activity.contract.review-required", ActivityDiagnosticSeverity.Warning)),
                _ => new ValidationVerdict(true, null)
            };
            var receipts = new InMemoryActivityPublicationReceiptStore();

            _publisher = new ActivityDefinitionPublisher(
                new SingleDefinitionStore(definition),
                new SingleAuthoringStore(authoring),
                new SingleDraftStore(draft),
                new PublicationStore(publications),
                new SingleLayoutStore(layout),
                new NoDependencyStore(),
                new FixedValidator(validation),
                new FixedBumpDiffer(candidate == Candidate.VersionBelowRequiredBump ? ActivityVersionBump.Major : ActivityVersionBump.None),
                new FixedCompiler(candidate == Candidate.CompilationFailed),
                new TestActivityPublishingAuthorizationContext(),
                new RecordingCommit(receipts, Commits),
                new ImmediateLockProvider(),
                new SequentialIdentityGenerator(),
                TimeProvider.System,
                receipts,
                candidate == Candidate.RuntimeConsumerMissing ? null : [new ReadyActivationStrategy()]);
        }

        public List<ActivityPublicationDesignMutation> Commits { get; } = [];

        public async Task<Outcome> PreflightAsync()
        {
            try
            {
                var view = await _publisher.PreflightAsync(new(DraftId, Revision, _headVersionId) { Version = _version });
                return view.IsPublishable ? Outcome.Accepted : Outcome.Refused(ActivityErrorCodes.PublicationInvalid, view.Diagnostics);
            }
            catch (ActivityPublicationRejectedException rejection)
            {
                return Outcome.Refused(rejection.ErrorCode, rejection.Diagnostics);
            }
        }

        /// <summary>
        /// Publishes with the token a preflight issued, exactly as a client would. A candidate preflight refuses
        /// outright never issues one, so publish gets a token that can bind nothing and must refuse on its own.
        /// </summary>
        public async Task<Outcome> PublishAsync()
        {
            string reviewToken;
            try
            {
                reviewToken = (await _publisher.PreflightAsync(new(DraftId, Revision, _headVersionId) { Version = _version })).ReviewToken;
            }
            catch (ActivityPublicationRejectedException)
            {
                reviewToken = "sha256:never-reviewed";
            }

            try
            {
                var receipt = await _publisher.PublishReviewedAsync(new(DraftId, Revision, _headVersionId, _version, reviewToken, "publish-1"));
                Assert.Equal(ActivityPublicationReceiptStatus.Applied, receipt.Status);
                return Outcome.Accepted;
            }
            catch (ActivityPublicationRejectedException rejection)
            {
                return Outcome.Refused(rejection.ErrorCode, rejection.Diagnostics);
            }
        }

        private static ActivityProviderManifest Provider() => new("test.provider", "1", JsonSerializer.SerializeToElement(new { }));

        private static ActivityDiagnostic Diagnostic(string code, ActivityDiagnosticSeverity severity) =>
            new(code, severity, code, new("ActivityDraft", DraftId, "definition-1", Revision: Revision));

        private static ActivityDefinitionVersionPublication Publication(string versionId, string version) => new()
        {
            Id = versionId,
            DefinitionId = "definition-1",
            DefinitionVersionId = versionId,
            Version = version,
            ActivityTypeKey = "test.activity",
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            Contract = new("1", [], [], []),
            Provider = Provider(),
            TemplateId = $"template-{versionId}",
            TemplateHash = $"sha256:{versionId}",
            SourceReferenceId = $"source-{versionId}",
            ProviderFingerprint = "provider-fingerprint",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = []
        };

        private sealed record ValidationVerdict(bool IsValid, ActivityDiagnostic? Diagnostic);

        private sealed class SingleDefinitionStore(ActivityDefinition definition) : IActivityDefinitionStore
        {
            public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
                id == definition.Id ? Task.FromResult(definition) : throw new KeyNotFoundException(id);

            public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private sealed class SingleAuthoringStore(ActivityDefinitionAuthoringState authoring) : IActivityDefinitionAuthoringStore
        {
            public Task<ActivityDefinitionAuthoringState?> FindAsync(string definitionId, CancellationToken cancellationToken = default) =>
                Task.FromResult(definitionId == authoring.DefinitionId ? authoring : null);

            public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private sealed class SingleDraftStore(ActivityDefinitionDraft draft) : IActivityDefinitionDraftStore
        {
            public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
                Task.FromResult(draftId == draft.Id ? draft : null);

            public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private sealed class PublicationStore(IReadOnlyList<ActivityDefinitionVersionPublication> publications) : IActivityDefinitionVersionPublicationStore
        {
            public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
                Task.FromResult(publications.SingleOrDefault(publication => publication.DefinitionVersionId == definitionVersionId));

            public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(publications.Where(publication => publication.DefinitionId == definitionId).ToArray());
        }

        private sealed class SingleLayoutStore(ActivityDefinitionDraftLayout layout) : IActivityDefinitionLayoutStore
        {
            public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) =>
                Task.FromResult(layout.DraftId == draftId ? layout : null);

            public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
                Task.FromResult<ActivityDefinitionVersionLayout?>(null);
        }

        private sealed class NoDependencyStore : IActivityDirectDependencyStore
        {
            public Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(string ownerVersionId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ActivityDependencyEdge>>([]);
        }

        private sealed class FixedValidator(ValidationVerdict verdict) : IActivityDraftValidator
        {
            public ValueTask<ActivityDraftValidation> ValidateAsync(ActivityDraftValidationRequest request, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(new ActivityDraftValidation(
                    request.DraftId,
                    request.Revision,
                    verdict.IsValid,
                    DateTimeOffset.UnixEpoch,
                    verdict.Diagnostic is null ? [] : [verdict.Diagnostic]));
        }

        private sealed class FixedBumpDiffer(ActivityVersionBump requiredBump) : IActivityVersionDiffer
        {
            public ValueTask<ActivityVersionDiff> DiffAsync(ActivityVersionDiffRequest request, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(new ActivityVersionDiff(
                    request.From,
                    request.To,
                    requiredBump == ActivityVersionBump.None ? ActivityVersionCompatibility.Identical : ActivityVersionCompatibility.Breaking,
                    requiredBump,
                    requiredBump != ActivityVersionBump.None,
                    new(request.FromProvider.ProviderKey, request.FromProvider.SchemaVersion, request.ToProvider.ProviderKey, request.ToProvider.SchemaVersion, false),
                    new(requiredBump == ActivityVersionBump.Major ? 1 : 0, 0, 0, 0),
                    [],
                    []));
        }

        /// <summary>
        /// Compiles to a template whose identity does not depend on the candidate version id, as the real compiler's
        /// behavioral hash does not, so the token preflight issues can bind the version publish allocates.
        /// </summary>
        private sealed class FixedCompiler(bool fails) : IActivityTemplateCompiler
        {
            public ValueTask<ActivityTemplateCompilerResult> CompileAsync(ActivityTemplateCompilerRequest request, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(fails
                    ? new ActivityTemplateCompilerResult(null, Measurements(), [], [Diagnostic("activity.provider.compilation-failed", ActivityDiagnosticSeverity.Error)])
                    : new ActivityTemplateCompilerResult(Template(), Measurements(), [], []));

            private static ActivityResourceMeasurements Measurements() => new(1, 1, 0, 1, 10, 20, 0);

            private static ExecutableActivityTemplate Template() => new(
                "activity-template-boundary",
                "sha256:boundary",
                new ExecutableNode(
                    "boundary",
                    "boundary",
                    Consumer,
                    "1",
                    new RuntimeActivityDescriptor(Consumer, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
                    new Dictionary<string, RuntimeInputBinding>(),
                    new Dictionary<string, RuntimeOutputCapture>(),
                    new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                [],
                [],
                [],
                "provider-fingerprint",
                new Dictionary<string, string>(),
                DateTimeOffset.UnixEpoch);
        }

        private sealed class ReadyActivationStrategy : IActivityActivationStrategy
        {
            public string ConsumerKey => Consumer;
            public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
            public bool RequiresInputHydration => false;

            public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("Publication only reads what the strategy supports.");
        }

        private sealed class RecordingCommit(IActivityPublicationReceiptStore receipts, List<ActivityPublicationDesignMutation> commits) :
            ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>
        {
            public async Task<ActivityPublicationResult> ExecuteAsync(
                ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
                CancellationToken cancellationToken = default)
            {
                commits.Add(commit.Design);
                Assert.True(await receipts.TryCreateAsync(commit.Receipt, cancellationToken));
                return new(
                    commit.Design.DefinitionId,
                    commit.Design.Publication.DefinitionVersionId,
                    commit.Design.DraftId,
                    commit.ExecutableTemplate.TemplateId,
                    commit.SourceReference.SourceReferenceId,
                    commit.Design.Publication.PublishedAt);
            }
        }

        private sealed class SequentialIdentityGenerator : IIdentityGenerator
        {
            private int _next;
            public string Generate() => Interlocked.Increment(ref _next).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class ImmediateLockProvider : IDistributedLockProvider
        {
            public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
            public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
            public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

            private sealed class Handle : IDistributedSynchronizationHandle
            {
                public CancellationToken HandleLostToken => CancellationToken.None;
                public void Dispose() { }
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }
    }
}
