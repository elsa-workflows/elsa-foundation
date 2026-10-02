using System.Reflection;
using System.Text.RegularExpressions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The credential-literal rule (spec 188, FR-008, T055) runs only in the application layer, and every application-layer
/// writer of workflow definition state takes the rule's contract, <see cref="ICredentialLiteralValidator"/> (a writer that
/// refuses a whole request admits through <see cref="WorkflowStateAdmission.AdmitAsync"/>). These guards keep a writer
/// from bypassing it: every design command contract is classified; every constructor outside persistence that takes a
/// state-writing command also takes the rule, and the callers found are exactly the known inventory; every git exporter
/// takes the rule; promotion cannot be called without the admitted draft's hash; and every production file outside the
/// design persistence project that reaches the design EF context, which writes state around the commands, is classified
/// with a reason.
/// </summary>
/// <remarks>
/// Whether a caller calls the helper before its command is proved per entry point by the behavioral tests (T051 to T054);
/// these guards prove the helper cannot be left out of a caller's composition, and that a new caller or a new contract
/// turns red until someone classifies it.
/// </remarks>
public sealed class CredentialLiteralAdmissionCoverageTests
{
    private const string ContractsNamespace = "Elsa.Workflows.Design.Persistence.Core.Contracts";
    private const string DesignPersistenceRoot = "src/essentials/Workflows/Design/Persistence/";

    /// <summary>Design commands that write workflow state: every caller admits that state first.</summary>
    private static readonly Type[] AdmittedCommands =
    [
        typeof(IAddWorkflowDefinitionCommand),
        typeof(IUpdateDraftCommand),
        typeof(IAddWorkflowDefinitionVersionCommand),
        typeof(ISubmitWorkflowDefinitionCommand),
        typeof(IPromoteDraftToVersionCommand),
        typeof(IMaterializeWorkflowDefinitionVersionCommand),
        typeof(ICreateDraftCommand)
    ];

    /// <summary>Design commands that write workflow state but are exempt, each with the reason.</summary>
    private static readonly Dictionary<Type, string> ExemptCommands = new()
    {
        [typeof(ICloneDraftFromVersionCommand)] =
            "copies a stored version into a draft and adds no content; a copied credential literal is refused when the draft is saved, promoted or published"
    };

    /// <summary>Design commands that write no workflow state.</summary>
    private static readonly Type[] NotStateWritingCommands =
    [
        typeof(ISaveWorkflowDefinitionCommand),
        typeof(IMaterializeWorkflowDefinitionCommand),
        typeof(IDiscardDraftCommand),
        typeof(IDeleteWorkflowDefinitionPermanentlyCommand)
    ];

    /// <summary>The types outside persistence whose constructors take an admitted command: the six Design API callers and the reconciler.</summary>
    private static readonly string[] KnownCallers =
    [
        "src/essentials/Workflows/Design/Api/Endpoints/Definitions/Add/Endpoint.cs: Endpoint",
        "src/essentials/Workflows/Design/Api/Endpoints/Definitions/Submit/Endpoint.cs: Endpoint",
        "src/essentials/Workflows/Design/Api/Endpoints/Definitions/Update/Handler.cs: Handler",
        "src/essentials/Workflows/Design/Api/Endpoints/Drafts/Promote/Endpoint.cs: Endpoint",
        "src/essentials/Workflows/Design/Api/Endpoints/Drafts/Replace/Endpoint.cs: Endpoint",
        "src/essentials/Workflows/Design/Api/Endpoints/Versions/Add/Endpoint.cs: Endpoint",
        "src/essentials/Workflows/Design/Reconciliation/Services/WorkflowsVersionReconciler.cs: WorkflowsVersionReconciler"
    ];

    /// <summary>
    /// Every production file outside the design persistence project that reaches the design EF context or a design EF
    /// command directly, with what it does with them. A writer of workflow state here bypasses the design commands and so
    /// the coverage above; each one is listed with why it is not admitted.
    /// </summary>
    private static readonly Dictionary<string, string> DesignContextUsers = new()
    {
        ["src/essentials/Workflows/Dashboard/Persistence/EntityFrameworkCore/DependencyInjection/WorkflowPortfolioEntityFrameworkCoreRegistration.cs"] =
            "not a state write: composition",
        ["src/essentials/Workflows/Dashboard/Persistence/EntityFrameworkCore/Stores/EfWorkflowPortfolioDataSource.cs"] =
            "not a state write: reads definitions and drafts for the portfolio",
        ["src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/DependencyInjection/PublishingEntityFrameworkCoreRegistration.cs"] =
            "not a state write: composition",
        ["src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfActivityDependencyProjectionRebuildCoordinator.cs"] =
            "not a state write: reads drafts and versions to rebuild the dependency projection",
        ["src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfActivityUpgradeLane.cs"] =
            "not a state write itself: binds the stores the upgrade plan store reads and writes through",
        ["src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfActivityUpgradePlanStore.cs"] =
            "writes drafts, exempt: an activity upgrade re-points stored nodes to another activity version and adds no authored " +
            "content; a binding the new version declares a credential is refused when the draft is promoted or published",
        ["src/extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/Stores/EfReusableActivityImportCommand.cs"] =
            "writes imported Elsa 3 workflow versions, not admitted: the Elsa 3 collection import is not one of FR-008's entry " +
            "points; a credential literal it imports is refused when the version is published or exported (residual, tasks.md T090)"
    };

    [Fact]
    public void Every_design_command_contract_is_classified_once()
    {
        var classified = AdmittedCommands.Concat(ExemptCommands.Keys).Concat(NotStateWritingCommands).ToArray();
        var contracts = typeof(IUpdateDraftCommand).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.Namespace == ContractsNamespace && type.Name.EndsWith("Command", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(classified.Length, classified.Distinct().Count());
        Assert.Equal(
            contracts.Select(type => type.Name).Order(StringComparer.Ordinal),
            classified.Select(type => type.Name).Order(StringComparer.Ordinal));
        Assert.Contains(typeof(IPromoteDraftToVersionCommand), AdmittedCommands);
    }

    [Fact]
    public void Every_constructor_outside_persistence_that_takes_a_state_writing_command_takes_the_rule()
    {
        var admitted = AdmittedCommands.Select(type => type.Name).ToArray();
        var callers = new List<string>();
        var violations = new List<string>();
        foreach (var (file, text) in NonPersistenceSources())
        {
            var constructors = ConstructorParameterLists(text).ToArray();
            foreach (var (type, start, parameters) in constructors.Where(constructor => admitted.Any(command => Mentions(constructor.Parameters, command))))
            {
                callers.Add($"{file}: {type}");
                if (!Mentions(parameters, nameof(ICredentialLiteralValidator)))
                    violations.Add($"{file}: {type}'s constructor at {start} takes a state-writing design command without {nameof(ICredentialLiteralValidator)}.");
            }

            // A command reached any other way (service location, a method parameter, a field typed by hand) bypasses the
            // constructor check above.
            violations.AddRange(admitted
                .SelectMany(command => Regex.Matches(text, $@"\b{command}\b").Select(match => (Command: command, match.Index)))
                .Where(use => !constructors.Any(constructor => use.Index > constructor.Start && use.Index < constructor.Start + constructor.Parameters.Length))
                .Select(use => $"{file}: '{use.Command}' at {use.Index} is reached outside a constructor parameter list."));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(KnownCallers.Order(StringComparer.Ordinal), callers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_git_workflow_exporter_takes_the_rule()
    {
        var exporters = ProductionSources()
            .SelectMany(source => ConstructorParameterLists(source.Text)
                .Where(constructor => Implements(source.Text, constructor.Type, "IGitWorkflowExporter"))
                .Select(constructor => (source.File, constructor.Type, constructor.Parameters)))
            .ToArray();

        Assert.NotEmpty(exporters);
        Assert.All(exporters, exporter => Assert.True(
            Mentions(exporter.Parameters, nameof(ICredentialLiteralValidator)),
            $"{exporter.File}: {exporter.Type} exports workflow state without {nameof(ICredentialLiteralValidator)}."));
    }

    [Fact]
    public void Every_promotion_requires_the_admitted_drafts_state_hash()
    {
        var context = new NullabilityInfoContext();
        var methods = typeof(IPromoteDraftToVersionCommand).GetMethods().Where(method => method.Name == "Execute").ToArray();
        var violations = methods
            .Where(method => method.GetParameters().SingleOrDefault(parameter => parameter.Name == "expectedStateHash") is not { } hash
                             || hash.ParameterType != typeof(string)
                             || context.Create(hash).WriteState != NullabilityState.NotNull)
            .Select(method => $"{method} does not take a non-nullable string expectedStateHash.")
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_design_context_user_outside_the_design_persistence_project_is_classified()
    {
        var efCommands = DesignEfCommandNames();
        var users = ProductionSources()
            .Where(source => !source.File.StartsWith(DesignPersistenceRoot, StringComparison.Ordinal))
            .Where(source => Mentions(source.Text, "WorkflowsDesignDbContext") || efCommands.Any(command => Mentions(source.Text, command)))
            .Select(source => source.File);

        Assert.NotEmpty(efCommands);
        Assert.Equal(DesignContextUsers.Keys.Order(StringComparer.Ordinal), users.Order(StringComparer.Ordinal));
    }

    /// <summary>The EF implementations of the design command contracts, read from the design persistence project's source.</summary>
    private static string[] DesignEfCommandNames() =>
        ProductionSources()
            .Where(source => source.File.StartsWith(DesignPersistenceRoot, StringComparison.Ordinal))
            .SelectMany(source => Regex.Matches(source.Text, @"\bclass\s+(Ef\w+Command)\b").Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<(string File, string Text)> NonPersistenceSources() =>
        ProductionSources().Where(source => !source.File.Contains("/Persistence/", StringComparison.Ordinal));

    /// <summary>Every production source file under <c>src/</c>, its path repository-relative, with comments and string literals blanked.</summary>
    private static IEnumerable<(string File, string Text)> ProductionSources() =>
        ModuleRoots.ProductionSourceFiles(RepoRoot)
            .Select(path => (
                File: Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                Text: ArchitectureGuardTests.StripCommentsAndStringLiterals(File.ReadAllText(path))));

    /// <summary>
    /// The parameter list of every constructor declared in <paramref name="text"/>: a primary constructor
    /// (<c>class Name(...)</c>, <c>record Name(...)</c>) and an explicit one (<c>public Name(...)</c>), each with the type
    /// it belongs to and where its list starts.
    /// </summary>
    private static IEnumerable<(string Type, int Start, string Parameters)> ConstructorParameterLists(string text)
    {
        var declared = Regex.Matches(text, @"\b(?:class|record|struct)\s+(\w+)").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var primary = Regex.Matches(text, @"\b(?:class|record|struct)\s+(\w+)\s*(?:<[^>(]*>)?\s*\(");
        var explicitConstructors = Regex.Matches(text, @"\b(?:public|internal|protected|private)\s+(\w+)\s*\(")
            .Where(match => declared.Contains(match.Groups[1].Value));
        return primary.Concat(explicitConstructors)
            .Select(match => (Type: match.Groups[1].Value, Start: match.Index + match.Length - 1))
            .Select(constructor => (constructor.Type, constructor.Start, Parameters: BalancedParentheses(text, constructor.Start)));
    }

    /// <summary>The text from the parenthesis at <paramref name="open"/> to its matching close, inclusive.</summary>
    private static string BalancedParentheses(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            depth += text[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
                return text[open..(index + 1)];
        }

        throw new InvalidOperationException($"Unbalanced parameter list at {open}.");
    }

    private static bool Mentions(string text, string name) => Regex.IsMatch(text, $@"\b{name}\b");

    /// <summary>Whether the base list of class <paramref name="type"/> in <paramref name="text"/> names <paramref name="contract"/>.</summary>
    private static bool Implements(string text, string type, string contract) =>
        Regex.IsMatch(text, $@"\bclass\s+{type}\s*(?:<[^>]*>)?\s*(?:\([^{{]*?\))?\s*:[^{{]*\b{contract}\b");
}
