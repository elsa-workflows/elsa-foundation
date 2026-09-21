using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The post-migration seam's own rules (ADR 0076 D8, FR-054/FR-056), away from any module: a declaration is
/// either usable or refused by name, an audit reports without running, and a required action fails closed
/// naming <c>dotnet elsa persistence post-migrate</c> rather than being run.
/// </summary>
public sealed class EfPostMigrationActionsTests
{
    private const string Module = "Acme.Widgets";

    private readonly DbContext context = new(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options);

    [Fact]
    public void A_usable_declaration_is_constructed_once_per_type()
    {
        var actions = EfPostMigrationActions.Create(Module, [typeof(RequiredAction), typeof(SatisfiedAction)]);

        Assert.Collection(
            actions,
            action => Assert.IsType<RequiredAction>(action),
            action => Assert.IsType<SatisfiedAction>(action));
    }

    [Fact]
    public void An_empty_declaration_is_not_a_fault()
    {
        Assert.True(EfPostMigrationActions.TryCreate(Module, [], out var actions, out var faults));
        Assert.Empty(actions);
        Assert.Empty(faults);
    }

    [Theory]
    [InlineData(typeof(Uri), "does not implement IEfPostMigrationAction")]
    [InlineData(typeof(IEfPostMigrationAction), "is not a constructible type")]
    [InlineData(typeof(AbstractAction), "is not a constructible type")]
    [InlineData(typeof(ConstructorArgumentAction), "has no public parameterless constructor")]
    [InlineData(typeof(ThrowingConstructorAction), "could not be constructed")]
    [InlineData(typeof(BlankIdAction), "leaves Id blank")]
    public void A_declaration_this_build_cannot_use_is_refused_by_name(Type declared, string reason)
    {
        Assert.False(EfPostMigrationActions.TryCreate(Module, [declared], out _, out var faults));

        var fault = Assert.Single(faults);
        Assert.Contains($"'{Module}'", fault, StringComparison.Ordinal);
        Assert.Contains($"'{declared.Name}'", fault, StringComparison.Ordinal);
        Assert.Contains(reason, fault, StringComparison.Ordinal);
    }

    /// <summary>A null entry is reachable: <c>[EfModule]</c> takes a <c>Type[]</c>, and nothing stops a null in it.</summary>
    [Fact]
    public void A_null_declaration_is_refused_rather_than_dereferenced()
    {
        Assert.False(EfPostMigrationActions.TryCreate(Module, [null!], out _, out var faults));
        Assert.Contains("null post-migration action type", Assert.Single(faults), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two actions sharing an id would make every message about one ambiguous — the refusal that names it,
    /// the manifest entry, and <c>post-migrate</c>'s report of what ran.
    /// </summary>
    [Fact]
    public void Two_actions_sharing_an_id_are_refused()
    {
        Assert.False(EfPostMigrationActions.TryCreate(Module, [typeof(RequiredAction), typeof(SameIdAction)], out _, out var faults));
        Assert.Contains("2 post-migration actions with id 'required'", Assert.Single(faults), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_throws_naming_the_module_and_every_fault()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => EfPostMigrationActions.Create(Module, [typeof(Uri)]));

        Assert.Contains(Module, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Uri", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiredAsync_reports_only_the_actions_that_audit_as_required()
    {
        var required = new RequiredAction();
        var satisfied = new SatisfiedAction();

        var reported = await EfPostMigrationActions.RequiredAsync(context, [satisfied, required]);

        Assert.Same(required, Assert.Single(reported));
        Assert.Equal(1, required.Audits);
        Assert.Equal(1, satisfied.Audits);
        Assert.Equal(0, required.Runs);
        Assert.Equal(0, satisfied.Runs);
    }

    /// <summary>
    /// An audit that cannot answer must throw, never report "not required": the two are indistinguishable
    /// downstream, and one of them is a database nobody looked at.
    /// </summary>
    [Fact]
    public async Task An_audit_that_fails_propagates_instead_of_reporting_nothing_required()
    {
        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => EfPostMigrationActions.RequiredAsync(context, [new FailingAuditAction()]));

        Assert.Equal("the database could not be read", failure.Message);
    }

    [Fact]
    public async Task EnsureNotRequiredAsync_fails_closed_naming_post_migrate_and_runs_nothing()
    {
        var required = new RequiredAction();

        var failure = await Assert.ThrowsAsync<EfPostMigrationRequiredException>(
            () => EfPostMigrationActions.EnsureNotRequiredAsync(context, Module, "PostgreSql", [required]));

        Assert.Equal(Module, failure.Module);
        Assert.Equal(["required"], failure.ActionIds);
        Assert.Equal("dotnet elsa persistence post-migrate --modules Acme.Widgets --provider PostgreSql", failure.Command);
        Assert.Contains(failure.Command, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, required.Runs);
    }

    [Fact]
    public async Task EnsureNotRequiredAsync_is_silent_when_nothing_is_required()
    {
        var satisfied = new SatisfiedAction();

        await EfPostMigrationActions.EnsureNotRequiredAsync(context, Module, "Sqlite", [satisfied]);

        Assert.Equal(1, satisfied.Audits);
        Assert.Equal(0, satisfied.Runs);
    }

    /// <summary>A module with nothing declared never reaches an audit at all, so it never opens a query.</summary>
    [Fact]
    public async Task EnsureNotRequiredAsync_does_nothing_for_a_module_that_declares_nothing()
    {
        await EfPostMigrationActions.EnsureNotRequiredAsync(null!, Module, "Sqlite", []);
    }

    private abstract class CountingAction(bool required) : IEfPostMigrationAction
    {
        public int Audits { get; private set; }

        public int Runs { get; private set; }

        public abstract string Id { get; }

        public string Kind => "test";

        public string RequiredWhen => "always";

        public string Audit => "test";

        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default)
        {
            Audits++;
            return Task.FromResult(required);
        }

        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }

    private sealed class RequiredAction() : CountingAction(required: true)
    {
        public override string Id => "required";
    }

    private sealed class SameIdAction() : CountingAction(required: true)
    {
        public override string Id => "required";
    }

    private sealed class SatisfiedAction() : CountingAction(required: false)
    {
        public override string Id => "satisfied";
    }

    private sealed class FailingAuditAction : IEfPostMigrationAction
    {
        public string Id => "failing";
        public string Kind => "test";
        public string RequiredWhen => "never";
        public string Audit => "test";

        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("the database could not be read");

        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private abstract class AbstractAction : IEfPostMigrationAction
    {
        public string Id => "abstract";
        public string Kind => "test";
        public string RequiredWhen => "never";
        public string Audit => "test";
        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ConstructorArgumentAction(string id) : IEfPostMigrationAction
    {
        public string Id => id;
        public string Kind => "test";
        public string RequiredWhen => "never";
        public string Audit => "test";
        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingConstructorAction : IEfPostMigrationAction
    {
        public ThrowingConstructorAction() => throw new InvalidOperationException("this action cannot be built here");

        public string Id => "throwing";
        public string Kind => "test";
        public string RequiredWhen => "never";
        public string Audit => "test";
        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlankIdAction : IEfPostMigrationAction
    {
        public string Id => "";
        public string Kind => "test";
        public string RequiredWhen => "never";
        public string Audit => "test";
        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
