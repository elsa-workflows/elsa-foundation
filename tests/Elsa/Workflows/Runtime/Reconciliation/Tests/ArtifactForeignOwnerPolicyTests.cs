using System.Reflection;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// T124: the foreign-owner outcome is the <em>default</em>, not the whole story. A deployment may report the same
/// condition as a rejection through <see cref="IArtifactForeignOwnerPolicy"/> — and may never turn it into a
/// takeover.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the seam may and may not move.</b> Skip and reject differ in how the pass <em>reports</em>: outcome,
/// rejection kind, which of <c>OwnershipSkips</c> / <c>Rejections</c> the entry lands in, and therefore which of
/// the startup task's two messages an operator reads at boot. Neither moves the slot. That asymmetry is the point
/// of the contract, so both halves are asserted on every replacement test here: the report changed, the serving
/// state did not.
/// </para>
/// <para>
/// <b>The incumbent stays generic.</b> These tests use a second artifact-reconciliation source as the owner, not
/// publishing — reconciliation yields to whoever holds the slot and cannot ask who that is without the comparison
/// §E2.2 forbids. What the policy adds is that a <em>host</em> may ask, because the incumbent's own
/// <see cref="WorkflowActivationSource"/> is handed to it.
/// </para>
/// </remarks>
public sealed class ArtifactForeignOwnerPolicyTests : IDisposable
{
    private const string DefinitionId = "definition-invoice";
    private const string IncumbentSourceId = "another-mount";
    private const string IncumbentActivationId = "import:another-mount:definition-invoice:incumbent";
    private const string CandidateNodeId = "node-candidate";

    private static readonly WorkflowActivationSource Incumbent =
        WorkflowActivationSource.ArtifactReconciliation(IncumbentSourceId);

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-foreign-owner-policy",
        Guid.NewGuid().ToString("N"));

    public ArtifactForeignOwnerPolicyTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    /// <summary>
    /// A replacement that rejects changes what the pass reports — and changes nothing about what serves.
    /// </summary>
    /// <remarks>
    /// The assertion that makes this worth having is the pair. A test that only showed the replacement resolving,
    /// or only that the outcome flipped to <c>Rejected</c>, would be satisfied by a seam wired to nothing in
    /// particular; a test that only showed the slot unmoved would be satisfied by a seam that is never consulted.
    /// </remarks>
    [Fact]
    public async Task A_replacement_that_rejects_reports_a_rejection_and_still_does_not_move_the_slot()
    {
        var policy = new RecordingPolicy(ArtifactForeignOwnerDecision.Reject);
        await using var harness = ArtifactImportHarness.Build(
            _mount,
            services => services.Replace(ServiceDescriptor.Scoped<IArtifactForeignOwnerPolicy>(_ => policy)));

        var incumbent = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-incumbent"), DefinitionId, "1.0.0");
        await ArtifactImportHarness.GiveTheSlotToAsync(harness, Incumbent, IncumbentActivationId, incumbent);
        var owned = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);

        // Newer than what is serving, so latest-wins would activate it: ownership is what stops it, and the policy
        // only decides how that is written down.
        var candidate = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode(CandidateNodeId), DefinitionId, "2.0.0");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "invoice.json", ArtifactClosureFixture.Closure(candidate));

        var pass = await ArtifactImportHarness.ReconcileAsync(harness);

        // ---- The report changed --------------------------------------------------------------------------
        var entry = Assert.Single(pass.Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.ActivationConflict, entry.RejectionKind);
        Assert.Equal(WorkflowArtifactSkipReason.None, entry.SkipReason);
        Assert.Equal(1, pass.RejectedCount);
        Assert.True(pass.HasRejections);
        Assert.Same(entry, Assert.Single(pass.Rejections));
        // It leaves the skip channel entirely, so the startup task reports it as "not imported" rather than as
        // "imported but NOT activated" — two different instructions to an operator.
        Assert.Empty(pass.OwnershipSkips);
        // The owner is still named: a rejection an operator cannot act on would be worse than the skip it replaced.
        Assert.Contains(Incumbent.Describe(), entry.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains(DefinitionId, entry.Diagnostic!, StringComparison.Ordinal);

        // ---- The serving state did not -------------------------------------------------------------------
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(IncumbentActivationId, slot!.ActiveActivationId);
        Assert.Equal(Incumbent.SourceId, slot.Source!.SourceId);
        Assert.Equal(owned!.Revision, slot.Revision);
        Assert.Empty(await ArtifactImportHarness.ListServingBindingsAsync(
            harness,
            ArtifactClosureFixture.TriggerStimulusHash(CandidateNodeId)));
        // The unit still imported; a rejection here is a statement about ownership, not about the artifact.
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, candidate.Identity.ArtifactId));

        // ---- And it was asked with the real incumbent ----------------------------------------------------
        // The whole reason this reaches a policy is that a host may know what its own source ids mean, which it
        // cannot do if the runtime hands it an anonymised "somebody else".
        var context = Assert.Single(policy.Contexts);
        Assert.Equal(DefinitionId, context.WorkflowDefinitionId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, context.Incumbent.Kind);
        Assert.Equal(IncumbentSourceId, context.Incumbent.SourceId);
        Assert.Equal(ArtifactImportHarness.SourceId, context.Candidate.SourceId);
        Assert.False(context.Incumbent.IsSameOwnerAs(context.Candidate));
    }

    /// <summary>
    /// The default is skip, through the real composition — the guard against T118's behaviour changing because a
    /// seam was added under it.
    /// </summary>
    [Fact]
    public async Task The_composed_default_still_skips()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var incumbent = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-incumbent"), DefinitionId, "1.0.0");
        await ArtifactImportHarness.GiveTheSlotToAsync(harness, Incumbent, IncumbentActivationId, incumbent);
        var candidate = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode(CandidateNodeId), DefinitionId, "2.0.0");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "invoice.json", ArtifactClosureFixture.Closure(candidate));

        using (var scope = harness.Services.CreateScope())
            Assert.IsType<SkipArtifactForeignOwnerPolicy>(scope.ServiceProvider.GetRequiredService<IArtifactForeignOwnerPolicy>());

        var pass = await ArtifactImportHarness.ReconcileAsync(harness);

        var entry = Assert.Single(pass.Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, entry.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, entry.SkipReason);
        Assert.Equal(0, pass.RejectedCount);
        Assert.Same(entry, Assert.Single(pass.OwnershipSkips));
    }

    /// <summary>
    /// The default skips whoever the owner is. It is a policy about ownership, not about a particular owner.
    /// </summary>
    [Theory]
    [InlineData(WorkflowActivationSource.PublishingKind, null)]
    [InlineData(WorkflowActivationSource.ArtifactReconciliationKind, "another-mount")]
    [InlineData("some-future-source", "whatever")]
    public async Task The_default_policy_answers_skip_for_every_incumbent(string kind, string? sourceId)
    {
        var decision = await new SkipArtifactForeignOwnerPolicy().DecideAsync(
            new ArtifactForeignOwnerContext(
                DefinitionId,
                new WorkflowActivationSource(kind, sourceId),
                WorkflowActivationSource.ArtifactReconciliation(ArtifactImportHarness.SourceId)));

        Assert.Same(ArtifactForeignOwnerDecision.Skip, decision);
    }

    /// <summary>
    /// §2.6.2 / ADR 0033: <c>TryAdd</c> makes replacement first-wins, so a host registers its own before composing.
    /// </summary>
    [Fact]
    public void A_host_registered_policy_survives_the_feature()
    {
        var services = new ServiceCollection();
        services.AddScoped<IArtifactForeignOwnerPolicy, RejectingPolicy>();

        new JsonWorkflowArtifactReconciliationFeature
        {
            Options = { SourceId = "mounted-artifacts", FolderPath = Path.GetTempPath() },
        }.ConfigureServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IArtifactForeignOwnerPolicy));
        Assert.Equal(typeof(RejectingPolicy), descriptor.ImplementationType);
    }

    [Fact]
    public void The_feature_registers_a_resolvable_default_policy()
    {
        var services = new ServiceCollection();

        new JsonWorkflowArtifactReconciliationFeature
        {
            Options = { SourceId = "mounted-artifacts", FolderPath = Path.GetTempPath() },
        }.ConfigureServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IArtifactForeignOwnerPolicy));
        Assert.Equal(typeof(SkipArtifactForeignOwnerPolicy), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>
    /// <b>Takeover is unrepresentable, and this is the structural half of saying so.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A policy that could authorise reconciliation to seize a foreign-owned slot would break the never-reclaim
    /// half of T118 and let a shell reload silently revert an operator's publish. That boundary must not rest on a
    /// doc comment, so the answer type is a closed pair of instances behind a private constructor: no host can
    /// construct a third, and adding one in-tree fails here rather than passing quietly the way a new enum member
    /// would slip through every existing <c>switch</c>.
    /// </para>
    /// <para>
    /// The other two guards are elsewhere by nature: the call site sits downstream of a refusal that already
    /// happened (asserted behaviourally by every test above — the slot never moves), and the decision is consumed
    /// by a <c>static</c> mapping that the compiler will not let reach the injected coordinator.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_decision_type_is_a_closed_pair_that_cannot_grow_a_takeover()
    {
        var type = typeof(ArtifactForeignOwnerDecision);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));

        var values = type
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == type)
            .Select(property => property.GetValue(null))
            .Concat(type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == type)
                .Select(field => field.GetValue(null)))
            .ToArray();

        Assert.Equal(2, values.Length);
        Assert.Contains(ArtifactForeignOwnerDecision.Skip, values);
        Assert.Contains(ArtifactForeignOwnerDecision.Reject, values);
        Assert.NotSame(ArtifactForeignOwnerDecision.Skip, ArtifactForeignOwnerDecision.Reject);
    }

    /// <summary>§2.6.2 minimalism: one member, no configuration surface to drift.</summary>
    [Fact]
    public void The_contract_has_exactly_one_member()
    {
        var members = typeof(IArtifactForeignOwnerPolicy).GetMembers(BindingFlags.Public | BindingFlags.Instance);

        var member = Assert.Single(members);
        Assert.Equal(nameof(IArtifactForeignOwnerPolicy.DecideAsync), member.Name);
    }

    /// <summary>Records what it was asked, so "the incumbent reached the policy" is a fact rather than a hope.</summary>
    private sealed class RecordingPolicy(ArtifactForeignOwnerDecision answer) : IArtifactForeignOwnerPolicy
    {
        public List<ArtifactForeignOwnerContext> Contexts { get; } = [];

        public ValueTask<ArtifactForeignOwnerDecision> DecideAsync(
            ArtifactForeignOwnerContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(answer);
        }
    }

    private sealed class RejectingPolicy : IArtifactForeignOwnerPolicy
    {
        public ValueTask<ArtifactForeignOwnerDecision> DecideAsync(
            ArtifactForeignOwnerContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ArtifactForeignOwnerDecision.Reject);
    }
}
