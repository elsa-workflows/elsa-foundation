using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.Architecture.Tests;

/// <summary>
/// What one pass over a set of sources found: constants, checks, declarations, upcasters, handles and stamps. Shared
/// by every <c>EfSchemaFamily*GuardTests</c> class (spec 180), so the tree is scanned once per concern rather than
/// once per test class.
/// </summary>
/// <remarks>
/// It reads syntax only, and resolves a family or version when it is a string literal or a <c>Type.Constant</c> whose
/// <c>const string</c> initializer is a literal. The version rule this guard had before the chain landed, which held a
/// check's version to its declaration, is gone: a check states no version any more, it reads the family's chain.
/// </remarks>
internal sealed class SchemaFamilyScan
{
    private static readonly string[] CheckMethods = ["EnsureReadable", "Readable", "NotReadable", "IsReadable", "EnsureCurrent"];
    private static readonly string[] DeclarationNames = ["EfSchemaFamily", "EfSchemaFamilyAttribute"];
    private static readonly string[] UpcasterNames = ["EfSchemaUpcaster", "EfSchemaUpcasterAttribute"];
    private static readonly string[] ContentNames = ["EfSchemaContent", "EfSchemaContentAttribute"];
    private static readonly string[] IntegrityNames = ["EfSchemaIntegrity", "EfSchemaIntegrityAttribute"];

    private readonly Dictionary<string, string?> _constants = new(StringComparer.Ordinal);
    private readonly List<(string Location, string Class, string Value)> _familyConstants = [];
    private readonly List<(string Location, string Path, string Class, ExpressionSyntax? From, ExpressionSyntax? To)> _upcasters = [];

    public List<(string Location, ExpressionSyntax Family)> Checks { get; } = [];

    public List<(string Location, ExpressionSyntax Family, ExpressionSyntax Version, string[] Upcasters)> Declarations { get; } = [];

    public List<(string Location, string Class, string? AssemblyOf, ExpressionSyntax Family)> Handles { get; } = [];

    public List<(string Location, ExpressionSyntax Value)> Stamps { get; } = [];

    public List<(string Location, string Context, string? FamilyClass)> MaterializedFamilies { get; } = [];

    /// <summary>
    /// Every <c>[EfSchemaContent(family, typeof(Entity), columns...)]</c> and <c>[EfSchemaIntegrity(family,
    /// typeof(Entity), column, reason)]</c> declaration: where it is, the family, the entity type and columns it names,
    /// and for an integrity column the reason.
    /// </summary>
    public List<(string Location, string Path, ExpressionSyntax? Family, string? Entity, ExpressionSyntax[] Columns, bool Integrity, ExpressionSyntax? Reason)> ColumnDeclarations { get; } = [];

    /// <summary>
    /// Every concrete class deriving directly from <c>EfSchemaUpcasterProof&lt;TUpcaster, TValue&gt;(family, store)</c>:
    /// where it is, the upcaster it proves, and the family it names.
    /// </summary>
    public List<(string Location, string Path, string Upcaster, ExpressionSyntax? Family)> Proofs { get; } = [];

    /// <summary>Every upcaster type seen, with the versions its <c>[EfSchemaUpcaster(from, to)]</c> resolves to.</summary>
    public IReadOnlyList<(string Path, string Class, string? From, string? To)> UpcasterVersions() =>
        _upcasters.Select(upcaster => (upcaster.Path, upcaster.Class, Resolve(upcaster.From), Resolve(upcaster.To))).ToArray();

    private readonly List<(string Path, CompilationUnitSyntax Root)> _roots = [];
    private readonly List<(string Location, string Path, string Row, string Column, SyntaxNode? Member)> _memberWrites = [];
    private readonly List<(string Name, int Arity, string[] Parameters, SyntaxNode Node)> _methods = [];
    private readonly HashSet<string> _stampedTypes = new(StringComparer.Ordinal);

    /// <summary>Each type's first base type, by simple name, as its base list names it.</summary>
    private readonly Dictionary<string, string> _baseTypes = new(StringComparer.Ordinal);

    /// <summary>Each type's own property types, by simple name, keyed by (declaring type, property).</summary>
    private readonly Dictionary<(string Type, string Property), string> _propertyTypes = new();
    private readonly List<(string Location, string Path, string Method, string Type, string Target, string Source, SyntaxNode Node)> _rowCopyCandidates = [];

    /// <summary>
    /// Every <c>Entry(target).CurrentValues.SetValues(...)</c> and <c>Update(target)</c> call found in a method or
    /// local function, where <c>target</c> names one of that member's own parameters: the two EF idioms that
    /// overwrite every mapped column of a tracked row at once, so the full-row-rewrite rule judges them the same way
    /// it judges an explicit member-to-member copy (spec 180, FR-014, 2026-09-28 note).
    /// </summary>
    private readonly List<(string Location, string Path, string Method, string Type, string Target, string Kind, SyntaxNode Node)> _fullReplaceCandidates = [];

    /// <summary>
    /// Every <c>target = new Type { A = ..., B = ... }</c> found in a method or local function, where <c>target</c>
    /// names one of that member's own parameters: a row replaced in place by a freshly built instance, the third full
    /// rewrite the rule recognises. The initializer, not just its columns, is kept, so a <c>SchemaVersion</c>
    /// assignment inside it can be told from one added by a separate statement in the same member.
    /// </summary>
    private readonly List<(string Location, string Path, string Method, string Type, string Target, InitializerExpressionSyntax Initializer, SyntaxNode Node)> _replacementCandidates = [];

    /// <summary>
    /// Every <c>new Type { ... }</c> object creation, anywhere, whose initializer assigns <c>SchemaVersion</c>: a
    /// fresh row (a new local, a field, an argument or a return value), not only a row replacing an existing
    /// parameter the way <see cref="_replacementCandidates"/> tracks. The restamp-completeness rule holds it to the
    /// same bar as a dotted stamp (<c>row.SchemaVersion = ...</c>): every declared content column of the type it
    /// creates, assigned in the initializer or, when the creation names a row (a local or an assignment target), by
    /// a later write to that row in the same member (#2144).
    /// </summary>
    private readonly List<(string Location, string Path, SyntaxNode? Member, string Type, string? Row, InitializerExpressionSyntax Initializer, SyntaxNode Node)> _stampedCreations = [];
    private IReadOnlySet<(string Name, int Arity)>? _upcastingMethods;
    private IReadOnlySet<(string Name, int Arity, int Parameter)>? _stampingMethods;
    private IReadOnlyList<(string Location, string Path, string? Family, string? Entity, string? Column, bool Integrity, string? Reason)>? _declaredColumns;
    private IReadOnlySet<string>? _contentColumns;

    /// <summary>
    /// Every method that hands one of its parameters to a chain's <c>Upcast</c>, as an argument or as a column value in
    /// one of its <c>(column, value)</c> arguments, directly or through another such method, by name and arity: the store
    /// helpers a content column could be read through. A chain's own <c>Upcast</c>, of any arity, is upcasting without
    /// being listed (<see cref="IsUpcasting"/>).
    /// </summary>
    public IReadOnlySet<(string Name, int Arity)> UpcastingMethods => _upcastingMethods ??= Fixpoint<(string Name, int Arity)>(
        [],
        (method, known) => method.Node.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation =>
            IsUpcasting(invocation, known) &&
            invocation.ArgumentList.Arguments.SelectMany(ValuesOf).Any(value => value is IdentifierNameSyntax identifier && method.Parameters.Contains(identifier.Identifier.ValueText)))
            ? [(method.Name, method.Parameters.Length)]
            : []);

    /// <summary>True when <paramref name="invocation"/> is a chain's <c>Upcast</c>, or a helper of <paramref name="helpers"/>.</summary>
    private static bool IsUpcasting(InvocationExpressionSyntax invocation, IReadOnlySet<(string Name, int Arity)> helpers) =>
        InvokedName(invocation) == "Upcast" || helpers.Contains((InvokedName(invocation), invocation.ArgumentList.Arguments.Count));

    private bool IsUpcasting(InvocationExpressionSyntax invocation) => IsUpcasting(invocation, UpcastingMethods);

    /// <summary>The values an argument hands over: itself, or each element of a tuple argument.</summary>
    private static IEnumerable<ExpressionSyntax> ValuesOf(ArgumentSyntax argument) =>
        argument.Expression is TupleExpressionSyntax tuple ? tuple.Arguments.Select(element => element.Expression) : [argument.Expression];

    /// <summary>
    /// Every <c>(column, value)</c> argument of a chain's <c>Upcast</c>: the row's content column the value is read from,
    /// as its <c>nameof</c>, literal or constant names it, and the value.
    /// </summary>
    private IEnumerable<(TupleExpressionSyntax Tuple, string? Column, ExpressionSyntax Value)> ColumnArguments(InvocationExpressionSyntax upcast) =>
        upcast.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<TupleExpressionSyntax>()
            .Where(tuple => tuple.Arguments.Count == 2)
            .Select(tuple => (tuple, ColumnName(tuple.Arguments[0].Expression), tuple.Arguments[1].Expression));

    /// <summary>
    /// Every method that stamps the row one of its parameters holds, directly or through another such method, by name,
    /// arity and the parameter's position: <c>Upgrade(row)</c>, a <c>Prepare(row, ...)</c>, or a helper calling either.
    /// </summary>
    public IReadOnlySet<(string Name, int Arity, int Parameter)> StampingMethods => _stampingMethods ??= Fixpoint<(string Name, int Arity, int Parameter)>(
        [],
        (method, known) => method.Parameters
            .Select((parameter, index) => (parameter, index))
            .Where(item => StampsRow(method.Node, item.parameter, known))
            .Select(item => (method.Name, method.Parameters.Length, item.index))
            .ToArray());

    /// <summary>
    /// Every content and integrity column the tree declares, resolved: the family, the entity type, the column, whether
    /// it is an integrity column, and that column's reason. A part this guard cannot resolve reads null.
    /// </summary>
    public IReadOnlyList<(string Location, string Path, string? Family, string? Entity, string? Column, bool Integrity, string? Reason)> DeclaredColumns() =>
        _declaredColumns ??= ColumnDeclarations
            .SelectMany(declaration => (declaration.Columns.Length == 0 ? [null] : declaration.Columns.Cast<ExpressionSyntax?>())
                .Select(column => (declaration.Location, declaration.Path, Resolve(declaration.Family), declaration.Entity, Column: ColumnName(column),
                    declaration.Integrity, Reason: declaration.Reason is null ? null : Resolve(declaration.Reason))))
            .ToArray();

    /// <summary>
    /// The families EF materializes directly (<c>IEfSchemaVersionedContext</c>): the materialization interceptor reads
    /// their rows at the current version alone, and their context stamps every row it writes, so their content columns
    /// meet the read and write rules by that mechanism rather than at each call site.
    /// </summary>
    public IReadOnlySet<string> MaterializedFamilyNames =>
        MaterializedFamilies
            .Select(context => context.FamilyClass is null ? null : _familyConstants.FirstOrDefault(constant => constant.Class == context.FamilyClass).Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The content columns the read and write rules hold to the chain and the stamp: every column a family's
    /// <c>[EfSchemaContent]</c> declares, apart from those of a family EF materializes directly. Matched by name, since
    /// this guard reads syntax: a name one family declares content is held to both rules wherever it is read or written,
    /// the loud direction.
    /// </summary>
    public IReadOnlySet<string> ContentColumns => _contentColumns ??= HeldContent().Select(column => column.Column).ToHashSet(StringComparer.Ordinal);

    /// <summary>The declared content columns the rules hold, each with the path of its declaration.</summary>
    private IEnumerable<(string Column, string Path)> HeldContent()
    {
        var materialized = MaterializedFamilyNames;
        return DeclaredColumns()
            .Where(column => !column.Integrity && column.Column is not null && (column.Family is null || !materialized.Contains(column.Family)))
            .Select(column => (column.Column!, column.Path));
    }

    /// <summary>In-place writes of a content column: the receiver, the column, and whether the same member restamps it.</summary>
    public IReadOnlyList<(string Location, string Row, string Column, bool Restamped)> ContentRewrites =>
        _memberWrites
            .Where(write => ContentColumns.Contains(write.Column))
            .Select(write => (write.Location, write.Row, write.Column, Restamped: write.Member is not null && StampsRow(write.Member, write.Row, StampingMethods)))
            .ToArray();

    /// <summary>
    /// Every read of a declared content column (<see cref="ContentColumns"/>) in the files <paramref name="isReader"/>
    /// accepts, whose source <paramref name="sees"/> the declaration from, that does not go through the family's chain;
    /// and the exemptions that matched nothing. A read goes through the chain when it is a column value of a chain's
    /// <c>Upcast</c>, <c>(nameof(r.C), r.C)</c>, or an argument of an upcasting helper. It deserializes nothing when it is
    /// a <c>nameof</c>, the target of a write, a copy into the same column of another row (<c>a.C = b.C</c>, or
    /// <c>C = b.C</c> in an initializer, whose target the restamp rule then holds to a stamp), or a column named in model
    /// configuration (<c>Property(x =&gt; x.C)</c>). A presence check (<c>is null</c>, <c>string.IsNullOrWhiteSpace</c>)
    /// is a read like any other: a step sees the whole row and may fill a column or clear one (#2144), so whether a column
    /// holds anything is read from the upcast row. Anything else is a violation unless <paramref name="exempt"/> names it
    /// by file, member, the read, and what consumes it (<see cref="Consumer"/>), with the reason.
    /// </summary>
    public (IReadOnlyList<string> Violations, IReadOnlyList<string> UnusedExemptions, int Reads) ContentReads(
        Func<string, bool> isReader,
        Func<string, string, bool> sees,
        IReadOnlyDictionary<(string File, string Member, string Read, string Consumer), string> exempt)
    {
        var declarations = HeldContent().ToLookup(column => column.Column, column => column.Path, StringComparer.Ordinal);
        var reads = _roots.Where(source => isReader(source.Path))
            .SelectMany(source => source.Root.DescendantNodes().OfType<ExpressionSyntax>()
                .Where(access => ColumnOf(access) is { } column && declarations[column].Any(declaration => sees(Normalize(source.Path), declaration)) &&
                                 !IsWriteTarget(access) && !InNameOf(access))
                .Select(access => (source.Path, Access: access)))
            .ToArray();
        var unchained = reads
            .Where(read => !IsChained(read.Access) && !IsCopy(read.Access) && !IsModelConfiguration(read.Access))
            .Select(read => (read.Path, read.Access, Key: (File: Path.GetFileName(read.Path), Member: MemberName(read.Access), Read: read.Access.ToString(), Consumer: Consumer(read.Access))))
            .ToArray();
        var violations = unchained
            .Where(read => !exempt.ContainsKey(read.Key))
            .Select(read => $"{Locate(read.Path, read.Access)}: reads '{read.Access}' in '{read.Key.Member}' ({read.Key.Consumer}) without its family's chain; " +
                            "pass it to the chain's Upcast, or a helper that does, with the row's stamp (spec 180, FR-009).");
        var unused = exempt.Keys
            .Where(key => !unchained.Any(read => read.Key == key))
            .Select(key => $"{key.File}, {key.Member}, {key.Read}, {key.Consumer}: exempts a read that no longer exists; remove the exemption.");
        return (Ordered(violations), Ordered(unused), reads.Length);
    }

    /// <summary>
    /// Every content or integrity declaration this guard cannot hold the tree to: one naming a family no
    /// <c>[EfSchemaFamily]</c> declares, one whose entity or column is not a <c>typeof</c>, <c>nameof</c>, literal or
    /// constant, and an integrity column with no reason.
    /// </summary>
    public IReadOnlyList<string> ColumnDeclarationViolations()
    {
        var families = Declared().Select(declaration => declaration.Family).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return Ordered(DeclaredColumns().SelectMany(column =>
        {
            var kind = column.Integrity ? "an integrity column" : "a content column";
            var problems = new List<string>();
            if (column.Family is null || !families.Contains(column.Family))
                problems.Add($"{column.Location}: declares {kind} of '{column.Family ?? "a family this guard cannot resolve"}', which no [EfSchemaFamily] declares.");
            if (column.Entity is null || column.Column is null)
                problems.Add($"{column.Location}: declares {kind} this guard cannot resolve; name the entity with typeof and the column with nameof, a literal or a constant.");
            if (column.Integrity && string.IsNullOrWhiteSpace(column.Reason))
                problems.Add($"{column.Location}: declares an integrity column '{column.Entity}.{column.Column}' with no reason; record why it is compared as stored bytes.");
            return problems;
        }));
    }

    /// <summary>
    /// What a store hands a family's chain, held to the declaration (spec 180, FR-009; #2144):
    /// <list type="bullet">
    /// <item>every column a read names, by <c>nameof</c>, is one the family declares content: the chain the call goes
    /// through when it calls <c>&lt;Module&gt;.Chain.Upcast</c> directly, any family otherwise. This keeps the
    /// declaration at least as complete as the call sites;</item>
    /// <item>each <c>(column, value)</c> reads the column it names, so no value is upcast as another column's;</item>
    /// <item>a <c>Upcast&lt;TEntity&gt;</c> passes exactly the columns the family declares for <c>TEntity</c>'s table,
    /// so no read upcasts a row in part, leaving a column it did not pass at the row's stamp;</item>
    /// <item>and the row it reads the columns of is a <c>TEntity</c>, where this guard can tell the row's declared type,
    /// so no row is upcast as another table's, whose steps would transform it as that table's.</item>
    /// </list>
    /// The chain refuses the first three at run time too, on every read at every version; the build fails on them first,
    /// and on the fourth, which two tables declaring the same columns would hide from the run-time check.
    /// </summary>
    public IReadOnlyList<string> UpcastDeclarationViolations()
    {
        var content = DeclaredColumns().Where(column => !column.Integrity).ToArray();
        var violations = new List<string>();
        foreach (var (path, root) in _roots)
        {
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsUpcasting))
            {
                var family = invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Upcast", Expression: var handle } ? FamilyOfHandle(handle) : null;
                var location = Locate(path, invocation);
                var columns = ColumnArguments(invocation).ToArray();
                var named = invocation.ArgumentList.Arguments.Select(argument => NameOf(argument.Expression))
                    .Concat(columns.Select(column => column.Column))
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal);
                violations.AddRange(named
                    .Where(column => !content.Any(declared => declared.Column == column && (family is null || declared.Family == family)))
                    .Select(column => $"{location}: upcasts '{column}' through " +
                                      (family is null ? "a family's chain, but no family declares it content" : $"the '{family}' chain, but '{family}' does not declare it content") +
                                      "; declare it with [EfSchemaContent] (spec 180, FR-009)."));
                violations.AddRange(columns
                    .Where(column => column.Column is null || ColumnOf(column.Value) is { } read && read != column.Column)
                    .Select(column => column.Column is null
                        ? $"{location}: passes '{column.Tuple}', whose column this guard cannot resolve; name it with nameof, a literal or a constant (spec 180, FR-009)."
                        : $"{location}: passes '{column.Value}' as column '{column.Column}'; a (column, value) pair reads the column it names (spec 180, FR-009)."));

                if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments: [var typeArgument] } })
                    continue;
                var entity = Rightmost(typeArgument) ?? typeArgument.ToString();
                var declared = content.Where(column => column.Entity == entity && column.Column is not null && (family is null || column.Family == family))
                    .Select(column => column.Column!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var passed = columns.Select(column => column.Column).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (declared.Length == 0)
                    violations.Add($"{location}: upcasts a row of '{entity}', which {(family is null ? "no family" : $"'{family}'")} declares no content columns for; " +
                                   "declare them with [EfSchemaContent] (spec 180, FR-009).");
                else if (!declared.SequenceEqual(passed, StringComparer.Ordinal))
                    violations.Add($"{location}: passes {Quoted(passed)} of '{entity}', but its family declares {Quoted(declared)}; a read upcasts every " +
                                   "declared content column of a row together, so no row is upcast in part (spec 180, FR-009; #2144).");

                foreach (var (row, type) in columns.Select(column => column.Value).OfType<MemberAccessExpressionSyntax>()
                             .Select(access => access.Expression).OfType<IdentifierNameSyntax>()
                             .Select(identifier => identifier.Identifier.ValueText).Distinct(StringComparer.Ordinal)
                             .Select(row => (row, type: DeclaredType(invocation, row)))
                             .Where(candidate => candidate.type is not null && candidate.type != entity && !DerivesFrom(entity, candidate.type)))
                {
                    violations.Add($"{location}: upcasts '{row}', a '{type}', as a row of '{entity}'; a row is upcast as its own table's, whose steps " +
                                   "are the ones written for it (spec 180, FR-009; #2144).");
                }
            }
        }

        return Ordered(violations);
    }

    /// <summary>
    /// Every <c>Upcast&lt;TEntity&gt;(stamp, (column, value), ...)</c> a store reads a row with: the calls the whole-row rule
    /// of <see cref="UpcastDeclarationViolations"/> judges, so its floor can tell a rule that passes from one that saw nothing.
    /// </summary>
    public IReadOnlyList<string> RowUpcasts() =>
        _roots.SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "Upcast" } } &&
                                     ColumnArguments(invocation).Any())
                .Select(invocation => Locate(source.Path, invocation)))
            .ToArray();

    private static string Quoted(IEnumerable<string> columns) => columns.Any() ? string.Join(", ", columns.Select(column => $"'{column}'")) : "no columns";

    /// <summary>
    /// The declared type of <paramref name="name"/> where <paramref name="node"/> reads it: a parameter of an enclosing
    /// method, local function or explicitly typed lambda, or a local declared with a type rather than <c>var</c>; null
    /// when the syntax does not say.
    /// </summary>
    private static string? DeclaredType(SyntaxNode node, string name)
    {
        // AncestorsAndSelf, not Ancestors: the restamp-completeness rule calls this with the enclosing member itself
        // (the row's own stamp gives it no narrower node to start from), so the member's own parameters must be in
        // reach, not just its ancestors' (#2144).
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var parameters = ancestor switch
            {
                BaseMethodDeclarationSyntax method => method.ParameterList.Parameters,
                LocalFunctionStatementSyntax local => local.ParameterList.Parameters,
                ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
                SimpleLambdaExpressionSyntax lambda => SyntaxFactory.SeparatedList([lambda.Parameter]),
                _ => default(SeparatedSyntaxList<ParameterSyntax>?)
            };
            // The nearest declaration of the name decides, typed or not: an untyped lambda parameter hides an outer one.
            if (parameters?.FirstOrDefault(parameter => parameter.Identifier.ValueText == name) is { } declared)
                return declared.Type is { } parameterType ? Rightmost(parameterType) ?? parameterType.ToString() : null;
            if (ancestor is BlockSyntax block &&
                block.DescendantNodes().OfType<VariableDeclarationSyntax>()
                    .FirstOrDefault(declaration => declaration.Variables.Any(variable => variable.Identifier.ValueText == name)) is { Type: var localType })
                return localType.IsVar ? null : Rightmost(localType) ?? localType.ToString();
            if (ancestor is MemberDeclarationSyntax and not BaseMethodDeclarationSyntax)
                break;
        }

        return null;
    }

    /// <summary>True when <paramref name="type"/> derives from <paramref name="baseType"/> through base lists this scan read.</summary>
    private bool DerivesFrom(string type, string baseType)
    {
        for (var current = type; _baseTypes.TryGetValue(current, out var next);)
        {
            if (next == baseType)
                return true;
            current = next;
        }

        return false;
    }

    /// <summary>Every family a check names through a handle, or a <c>SchemaFamily</c> constant holds.</summary>
    public IReadOnlySet<string> NamedFamilies =>
        Checks.Select(check => FamilyOfHandle(check.Family)).OfType<string>()
            .Concat(_familyConstants.Select(constant => constant.Value))
            .ToHashSet(StringComparer.Ordinal);

    public static SchemaFamilyScan Of(IEnumerable<(string Path, string Text)> sources)
    {
        var scan = new SchemaFamilyScan();
        foreach (var (path, text) in sources)
            scan.Read(path, CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot());
        return scan;
    }

    public IReadOnlyList<string> AllViolations() =>
    [
        .. DeclarationViolations(), .. HandleViolations(), .. ChainViolations(), .. UpcasterViolations(),
        .. MaterializedFamilyViolations(), .. StampViolations(), .. RestampViolations(), .. FullRowRewriteViolations(),
        .. RestampCompletenessViolations(),
        .. ColumnDeclarationViolations(), .. UpcastDeclarationViolations(),
        .. ContentReads(_ => true, (_, _) => true, new Dictionary<(string, string, string, string), string>()).Violations
    ];

    /// <summary>Every declared chain's steps, resolved: the family, and the versions each upcaster reads and produces.</summary>
    public IEnumerable<(string Location, string Family, string From, string To)> UpcasterSteps() =>
        ShippedUpcasters().Select(step => (step.Location, step.Family, step.From, step.To));

    /// <summary>Every declared chain's steps, with the upcaster type each is.</summary>
    public IEnumerable<(string Location, string Family, string Upcaster, string From, string To)> ShippedUpcasters() =>
        Declared()
            .Where(declaration => declaration.Family is not null)
            .SelectMany(declaration => declaration.Upcasters
                .Select(name => _upcasters.FirstOrDefault(upcaster => upcaster.Class == name))
                .Where(upcaster => upcaster.Class is not null)
                .Select(upcaster => (upcaster.Location, Family: declaration.Family!, upcaster.Class, From: Resolve(upcaster.From), To: Resolve(upcaster.To))))
            .Where(step => step.From is not null && step.To is not null)
            .Select(step => (step.Location, step.Family, step.Class, step.From!, step.To!));

    public IReadOnlyList<string> RestampViolations() =>
        Ordered(ContentRewrites
            .Where(rewrite => !rewrite.Restamped)
            .Select(rewrite => $"{rewrite.Location}: rewrites '{rewrite.Row}.{rewrite.Column}' but never stamps '{rewrite.Row}' in the same member.")
            .Concat(BulkContentWrites()
                .Where(write => !write.Restamped)
                .Select(write => $"{write.Location}: sets '{write.Column}' in a bulk update that never sets the row's SchemaVersion.")));

    /// <summary>
    /// Every method that copies two or more of a stamped type's non-key columns from one parameter of that type into
    /// another (<c>target.M = source.M</c>, for two or more <c>M</c>), and whether it also stamps the target: the
    /// full-rewrite half of the restamp rule the declared-content columns cannot carry for a row with no content, such
    /// as Identity's child rows (spec 180, FR-014, 2026-09-28 note). A "stamped type" is one declaring a
    /// <c>SchemaVersion</c> member; a method that copies at most one column - a revision-only bump, or a single field
    /// alongside it - is left alone, since the owner's amendment holds only a full rewrite or a row replacement to the
    /// stamp.
    /// </summary>
    public IReadOnlyList<(string Location, string Method, string Type, string Target, IReadOnlyList<string> CopiedColumns, bool Restamped, string Kind)> FullRowRewrites
    {
        get
        {
            var copies = _rowCopyCandidates
                .Where(candidate => _stampedTypes.Contains(candidate.Type))
                .Select(candidate => (candidate, Copied: CopiedColumns(candidate.Node, candidate.Target, candidate.Source)))
                .Where(item => item.Copied.Count >= 2)
                .Select(item => (item.candidate.Location, item.candidate.Method, item.candidate.Type, item.candidate.Target, item.Copied,
                    Restamped: StampsRow(item.candidate.Node, item.candidate.Target, StampingMethods), Kind: "Copy"));

            // SetValues and Update always overwrite every mapped column of the row they take, so there is no column
            // count to gate on the way the copy and initializer shapes are.
            var replaces = _fullReplaceCandidates
                .Where(candidate => _stampedTypes.Contains(candidate.Type))
                .Select(candidate => (candidate.Location, candidate.Method, candidate.Type, candidate.Target,
                    CopiedColumns: (IReadOnlyList<string>)["every mapped column"],
                    Restamped: StampsRow(candidate.Node, candidate.Target, StampingMethods), Kind: candidate.Kind));

            var initializers = _replacementCandidates
                .Where(candidate => _stampedTypes.Contains(candidate.Type))
                .Select(candidate => (candidate, Assigned: InitializerColumns(candidate.Initializer)))
                .Where(item => item.Assigned.Columns.Count >= 2)
                .Select(item => (item.candidate.Location, item.candidate.Method, item.candidate.Type, item.candidate.Target, item.Assigned.Columns,
                    Restamped: item.Assigned.SetsSchemaVersion || StampsRow(item.candidate.Node, item.candidate.Target, StampingMethods), Kind: "Initializer"));

            return copies.Concat(replaces).Concat(initializers).ToArray();
        }
    }

    public IReadOnlyList<string> FullRowRewriteViolations() =>
        Ordered(FullRowRewrites
            .Where(rewrite => !rewrite.Restamped)
            .Select(rewrite => rewrite.Kind switch
            {
                "SetValues" => $"{rewrite.Location}: '{rewrite.Method}' overwrites '{rewrite.Target}' of type '{rewrite.Type}' via " +
                                $"CurrentValues.SetValues but never stamps '{rewrite.Target}'; a full rewrite of a stamped row restamps to the " +
                                "write version (spec 180, FR-014, 2026-09-28 note).",
                "Update" => $"{rewrite.Location}: '{rewrite.Method}' calls Update on '{rewrite.Target}' of type '{rewrite.Type}' but never stamps " +
                            $"'{rewrite.Target}'; a full rewrite of a stamped row restamps to the write version (spec 180, FR-014, 2026-09-28 note).",
                "Initializer" => $"{rewrite.Location}: '{rewrite.Method}' replaces '{rewrite.Target}' of type '{rewrite.Type}' with a new instance " +
                                  $"assigning {rewrite.CopiedColumns.Count} columns ({string.Join(", ", rewrite.CopiedColumns)}) but never stamps " +
                                  $"'{rewrite.Target}'; a full rewrite of a stamped row restamps to the write version (spec 180, FR-014, 2026-09-28 note).",
                _ => $"{rewrite.Location}: '{rewrite.Method}' rewrites {rewrite.CopiedColumns.Count} columns of '{rewrite.Type}' " +
                     $"({string.Join(", ", rewrite.CopiedColumns)}) from its second parameter into '{rewrite.Target}' but never stamps " +
                     $"'{rewrite.Target}'; a full rewrite of a stamped row restamps to the write version, a write that only bumps a " +
                     "concurrency revision need not (spec 180, FR-014, 2026-09-28 note)."
            }));

    /// <summary>
    /// Every (member, row) this scan can prove rewrites the row wholesale by construction - a copy, SetValues, Update
    /// or object-initializer replacement, the same three shapes <see cref="FullRowRewrites"/> recognises - keyed by
    /// file and the enclosing member's span rather than by node identity, since two independent traversals of the
    /// same tree are not guaranteed to hand back the same node instance. The restamp-completeness rule exempts
    /// these: writing every mapped column already writes every declared content column too (spec 180, FR-014;
    /// #2144).
    /// </summary>
    private IReadOnlySet<(string Path, TextSpan Span, string Row)>? _fullRowRewriteTargets;
    private IReadOnlySet<(string Path, TextSpan Span, string Row)> FullRowRewriteTargets => _fullRowRewriteTargets ??= BuildFullRowRewriteTargets();

    private IReadOnlySet<(string Path, TextSpan Span, string Row)> BuildFullRowRewriteTargets()
    {
        var targets = new HashSet<(string, TextSpan, string)>();
        foreach (var candidate in _rowCopyCandidates.Where(candidate => _stampedTypes.Contains(candidate.Type) && CopiedColumns(candidate.Node, candidate.Target, candidate.Source).Count >= 2))
            targets.Add((candidate.Path, candidate.Node.FullSpan, candidate.Target));
        foreach (var candidate in _fullReplaceCandidates.Where(candidate => _stampedTypes.Contains(candidate.Type)))
            targets.Add((candidate.Path, candidate.Node.FullSpan, candidate.Target));
        foreach (var candidate in _replacementCandidates.Where(candidate => _stampedTypes.Contains(candidate.Type) && InitializerColumns(candidate.Initializer).Columns.Count >= 2))
            targets.Add((candidate.Path, candidate.Node.FullSpan, candidate.Target));
        return targets;
    }

    /// <summary>
    /// The declared type of <paramref name="row"/> as read from <paramref name="member"/>: a simple identifier's
    /// declared parameter or local type, or - for a chain of properties, such as <c>state.Entity</c> - that type's
    /// own declared property, resolved one segment at a time from <see cref="_propertyTypes"/>. Null once a segment
    /// this scan cannot resolve is reached.
    /// </summary>
    private string? RowEntityType(SyntaxNode member, string row)
    {
        var segments = row.Split('.');
        var type = DeclaredType(member, segments[0]);
        for (var index = 1; index < segments.Length && type is not null; index++)
            type = _propertyTypes.GetValueOrDefault((type, segments[index]));
        return type;
    }

    /// <summary>
    /// Every member that stamps a row directly (<c>row.SchemaVersion = ...</c>) without, by construction, rewriting
    /// the whole row, and does not assign every content column its row's entity type declares in the same member
    /// (spec 180, FR-014; #2144): the restamp rule's other half, so a stamp never describes content a column was
    /// left holding at its old value. This guard judges only a row this scan resolves to an entity with declared
    /// content; anything else is left to <see cref="RestampViolations"/> and <see cref="FullRowRewriteViolations"/>.
    /// </summary>
    public IReadOnlyList<string> RestampCompletenessViolations()
    {
        var content = DeclaredColumns().Where(column => !column.Integrity).ToArray();
        var wholeRow = FullRowRewriteTargets;
        var violations = new List<string>();
        foreach (var stamp in _memberWrites.Where(write => write.Column == "SchemaVersion" && write.Member is not null &&
                                                            !wholeRow.Contains((write.Path, write.Member!.FullSpan, write.Row))))
        {
            var entity = RowEntityType(stamp.Member!, stamp.Row);
            if (entity is null)
                continue;
            var declared = content.Where(column => column.Entity == entity && column.Column is not null)
                .Select(column => column.Column!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (declared.Length == 0)
                continue;
            var written = _memberWrites
                .Where(write => write.Path == stamp.Path && write.Member is not null && write.Member.FullSpan == stamp.Member!.FullSpan && write.Row == stamp.Row)
                .Select(write => write.Column)
                .ToHashSet(StringComparer.Ordinal);
            var missing = declared.Where(column => !written.Contains(column)).ToArray();
            if (missing.Length > 0)
                violations.Add($"{stamp.Location}: stamps '{stamp.Row}' but never assigns {Quoted(missing)} of its declared content in the same member; " +
                               "a restamp rewrites every declared content column from upcast values, so no column is left at the row's old stamp (spec 180, FR-014; #2144).");
        }

        // An object initializer that stamps the row it creates is judged the same way: every declared content
        // column of that type must be assigned in the initializer, or, when the creation names a row, by a later
        // write to that row in the same member. A creation the full-row-rewrite rule already counts - a reassignment
        // of a declared parameter with two or more assigned columns - is left to that rule instead (#2144).
        foreach (var creation in _stampedCreations.Where(creation => _stampedTypes.Contains(creation.Type) &&
                     !(creation.Row is not null && creation.Member is not null && wholeRow.Contains((creation.Path, creation.Member.FullSpan, creation.Row)))))
        {
            var declared = content.Where(column => column.Entity == creation.Type && column.Column is not null)
                .Select(column => column.Column!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (declared.Length == 0)
                continue;
            var written = InitializerColumns(creation.Initializer).Columns.ToHashSet(StringComparer.Ordinal);
            if (creation.Row is not null && creation.Member is not null)
                written.UnionWith(_memberWrites
                    .Where(write => write.Path == creation.Path && write.Member is not null && write.Member.FullSpan == creation.Member.FullSpan && write.Row == creation.Row)
                    .Select(write => write.Column));
            var missing = declared.Where(column => !written.Contains(column)).ToArray();
            if (missing.Length > 0)
                violations.Add($"{creation.Location}: creates '{creation.Type}' and stamps it in the initializer but never assigns {Quoted(missing)} of its " +
                               "declared content there" + (creation.Row is not null ? $" or by a later write to '{creation.Row}' in the same member" : "") +
                               "; a restamp rewrites every declared content column from upcast values, so no column is left at the row's old stamp (spec 180, FR-014; #2144).");
        }

        return Ordered(violations);
    }

    /// <summary>The distinct columns, other than Revision and SchemaVersion, an object initializer assigns, and whether it assigns SchemaVersion itself.</summary>
    private static (IReadOnlyList<string> Columns, bool SetsSchemaVersion) InitializerColumns(InitializerExpressionSyntax initializer)
    {
        var assigned = initializer.Expressions.OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Left is IdentifierNameSyntax)
            .Select(assignment => ((IdentifierNameSyntax)assignment.Left).Identifier.ValueText)
            .ToArray();
        var setsSchemaVersion = assigned.Contains("SchemaVersion", StringComparer.Ordinal);
        var columns = assigned.Where(column => column is not ("Revision" or "SchemaVersion")).Distinct(StringComparer.Ordinal).ToArray();
        return (columns, setsSchemaVersion);
    }

    /// <summary>The distinct columns, other than Revision, <paramref name="target"/> copies from <paramref name="source"/> in <paramref name="node"/>.</summary>
    private static IReadOnlyList<string> CopiedColumns(SyntaxNode node, string target, string source) =>
        node.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                                  assignment.Left is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax left } leftAccess && left.Identifier.ValueText == target &&
                                  assignment.Right is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax right } rightAccess && right.Identifier.ValueText == source &&
                                  leftAccess.Name.Identifier.ValueText == rightAccess.Name.Identifier.ValueText)
            .Select(assignment => ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.ValueText)
            .Where(column => column != "Revision")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every bulk update (<c>ExecuteUpdate</c>) that sets a declared content column, and whether the same update sets
    /// the row's stamp. Such an update bypasses the change tracker, so a family EF materializes directly, whose context
    /// stamps what it saves, is held to it too.
    /// </summary>
    public IReadOnlyList<(string Location, string Column, bool Restamped)> BulkContentWrites()
    {
        var content = DeclaredColumns().Where(column => !column.Integrity).Select(column => column.Column).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return _roots.SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(invocation => InvokedName(invocation) == "SetProperty" && SetTarget(invocation) is { } column && content.Contains(column))
                .Select(invocation => (
                    Location: Locate(source.Path, invocation),
                    Column: SetTarget(invocation)!,
                    Restamped: invocation.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault(update => InvokedName(update).StartsWith("ExecuteUpdate", StringComparison.Ordinal)) is { } update &&
                               update.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(set => InvokedName(set) == "SetProperty" && SetTarget(set) == "SchemaVersion"))))
            .ToArray();
    }

    /// <summary>
    /// The column a <c>SetProperty(x =&gt; x.Column, ...)</c> or <c>SetProperty(x =&gt; EF.Property&lt;T&gt;(x, name), ...)</c>
    /// sets, where the name is a literal, a constant, or the stamp's own <c>ColumnName</c> or <c>PropertyName</c>.
    /// </summary>
    private string? SetTarget(InvocationExpressionSyntax setProperty) =>
        setProperty.ArgumentList.Arguments.FirstOrDefault()?.Expression is LambdaExpressionSyntax { Body: var body }
            ? body switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                InvocationExpressionSyntax { ArgumentList.Arguments: [_, var name] } property when InvokedName(property) == "Property" =>
                    name.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ColumnName" or "PropertyName" } stamp &&
                    Rightmost(stamp.Expression) is "EfSchemaVersion" or "EfSchemaVersionMaterializationInterceptor"
                        ? "SchemaVersion"
                        : Resolve(name.Expression),
                _ => null
            }
            : null;

    public IReadOnlyList<string> DeclarationViolations()
    {
        var declared = Declared();
        var versions = declared
            .Where(declaration => declaration.Family is not null)
            .GroupBy(declaration => declaration.Family!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Version, StringComparer.Ordinal);

        var unresolved = declared
            .Where(declaration => declaration.Family is null || declaration.Version is null)
            .Select(declaration => $"{declaration.Location}: declares a family this guard cannot resolve; write its name and version as literals or constants.");
        var duplicated = declared
            .Where(declaration => declaration.Family is not null)
            .GroupBy(declaration => declaration.Family!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{string.Join(", ", group.Select(declaration => declaration.Location))}: '{group.Key}' is declared {group.Count()} times; a family has exactly one declaration.");
        var checks = Checks.SelectMany(check => IsContextChain(check.Family)
            ? Array.Empty<string>()
            : FamilyOfHandle(check.Family) is not { } family
                ? [$"{check.Location}: names its family through '{check.Family}', not through a family's chain handle ('<Module>.Chain'); FR-002 names a family by one declared value."]
                : versions.ContainsKey(family)
                    ? []
                    : [$"{check.Location}: '{family}' is checked here but no [EfSchemaFamily] declares it."]);
        var undeclaredConstants = _familyConstants
            .Where(constant => !versions.ContainsKey(constant.Value))
            .Select(constant => $"{constant.Location}: '{constant.Value}' is a SchemaFamily constant but no [EfSchemaFamily] declares it.");

        return Ordered(unresolved.Concat(duplicated).Concat(checks).Concat(undeclaredConstants));
    }

    public IReadOnlyList<string> HandleViolations()
    {
        var missing = _familyConstants
            .Where(constant => !Handles.Any(handle => handle.Class == constant.Class))
            .Select(constant => $"{constant.Location}: '{constant.Class}' holds no chain handle for family '{constant.Value}'.");
        var foreign = Handles
            .Where(handle => handle.AssemblyOf != handle.Class)
            .Select(handle => $"{handle.Location}: '{handle.Class}' resolves its chain from 'typeof({handle.AssemblyOf}).Assembly', not from its own assembly.");
        var wrongFamily = Handles
            .Select(handle => (handle, Family: Resolve(handle.Family), Own: _familyConstants.FirstOrDefault(constant => constant.Class == handle.Class).Value))
            .Where(item => item.Own is not null && item.Family != item.Own)
            .Select(item => $"{item.handle.Location}: '{item.handle.Class}'s chain handle resolves family '{item.Family}' but its class's SchemaFamily is '{item.Own}'.");
        return Ordered(missing.Concat(foreign).Concat(wrongFamily));
    }

    public IReadOnlyList<string> ChainViolations()
    {
        var violations = new List<string>();
        foreach (var declaration in Declared().Where(declaration => declaration.Family is not null && declaration.Upcasters.Length > 0))
        {
            var steps = declaration.Upcasters
                .Select(name => (Name: name, Upcaster: _upcasters.FirstOrDefault(upcaster => upcaster.Class == name)))
                .Select(step => (step.Name, Found: step.Upcaster.Class is not null, From: Resolve(step.Upcaster.From), To: Resolve(step.Upcaster.To)))
                .ToArray();
            foreach (var step in steps.Where(step => !step.Found || step.From is null || step.To is null))
                violations.Add($"{declaration.Location}: '{declaration.Family}' lists '{step.Name}', which carries no [EfSchemaUpcaster(from, to)] this guard can resolve.");
            for (var index = 1; index < steps.Length; index++)
            {
                if (steps[index - 1].To is { } produced && steps[index].From is { } read && produced != read)
                    violations.Add($"{declaration.Location}: '{declaration.Family}' has a gap: '{steps[index - 1].Name}' produces '{produced}' but '{steps[index].Name}' reads '{read}'.");
            }

            if (steps[^1].To is { } last && declaration.Version is { } current && last != current)
                violations.Add($"{declaration.Location}: '{declaration.Family}' ends its chain at '{last}', not at its current version '{current}'.");
            violations.AddRange(steps.Select(step => step.From).Append(declaration.Version).OfType<string>()
                .GroupBy(version => version, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{declaration.Location}: '{declaration.Family}' reaches version '{group.Key}' more than once, which makes its chain a branch or a cycle."));
        }

        return Ordered(violations);
    }

    public IReadOnlyList<string> UpcasterViolations() =>
        Ordered(_upcasters
            .Select(upcaster => (upcaster, Listings: Declarations.Count(declaration => declaration.Upcasters.Contains(upcaster.Class, StringComparer.Ordinal))))
            .Where(item => item.Listings != 1)
            .Select(item => $"{item.upcaster.Location}: '{item.upcaster.Class}' is listed by {item.Listings} [EfSchemaFamily] declarations; an upcaster belongs to exactly one."));

    public IReadOnlyList<string> MaterializedFamilyViolations()
    {
        var declared = Declared();
        return Ordered(MaterializedFamilies
            .Select(context => (context, Family: context.FamilyClass is null ? null : _familyConstants.FirstOrDefault(constant => constant.Class == context.FamilyClass).Value))
            .SelectMany(item => item.Family is null
                ? [$"{item.context.Location}: '{item.context.Context}' names a chain this guard cannot resolve; return a module class's Chain."]
                : declared.Where(declaration => declaration.Family == item.Family && declaration.Upcasters.Length > 0)
                    .Select(declaration => $"{item.context.Location}: '{item.Family}' is materialized directly by '{item.context.Context}' but declares {declaration.Upcasters.Length} upcaster(s) at {declaration.Location}.")));
    }

    public IReadOnlyList<string> StampViolations()
    {
        var current = Declarations.Select(declaration => Key(declaration.Version)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return Ordered(Stamps.SelectMany(stamp => stamp.Value switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                [$"{stamp.Location}: stamps the literal '{literal.Token.ValueText}'; stamp the family's SchemaVersion constant."],
            MemberAccessExpressionSyntax access when Key(access) is { } key && _constants.ContainsKey(key) && !current.Contains(key) =>
                [$"{stamp.Location}: stamps '{key}', which no [EfSchemaFamily] declaration names as its current version."],
            _ => Array.Empty<string>()
        }));
    }

    private (string Location, string? Family, string? Version, string[] Upcasters)[] Declared() =>
        Declarations.Select(declaration => (declaration.Location, Resolve(declaration.Family), Resolve(declaration.Version), declaration.Upcasters)).ToArray();

    private void Read(string path, CompilationUnitSyntax root)
    {
        _roots.Add((path, root));
        foreach (var node in root.DescendantNodes())
        {
            var (name, parameters) = node switch
            {
                MethodDeclarationSyntax method => (method.Identifier.ValueText, method.ParameterList),
                LocalFunctionStatementSyntax local => (local.Identifier.ValueText, local.ParameterList),
                _ => ((string?)null, (ParameterListSyntax?)null)
            };
            if (name is not null && parameters is not null)
                _methods.Add((name, parameters.Parameters.Count, parameters.Parameters.Select(parameter => parameter.Identifier.ValueText).ToArray(), node));

            // A method or local function with two parameters of the same declared type is a candidate row-copy
            // helper (Apply(target, source)): the full-row-rewrite rule judges it once every stamped type is known.
            if (parameters is { Parameters: [var first, var second] } &&
                first.Type is { } firstType && second.Type is { } secondType &&
                (Rightmost(firstType) ?? firstType.ToString()) is { } typeName && typeName == (Rightmost(secondType) ?? secondType.ToString()))
                _rowCopyCandidates.Add((Locate(path, node), Normalize(path), name!, typeName, first.Identifier.ValueText, second.Identifier.ValueText, node));

            // SetValues, Update and an in-place object-initializer replacement, over any parameter of this member: the
            // three other ways a stamped row is overwritten wholesale that the two-parameter copy shape above cannot
            // see, since none of them takes the replacement as a second, same-typed parameter.
            if (parameters is not null)
            {
                var declaredTypes = parameters.Parameters
                    .Where(parameter => parameter.Type is not null)
                    .ToDictionary(
                        parameter => parameter.Identifier.ValueText,
                        parameter => Rightmost(parameter.Type!) ?? parameter.Type!.ToString(),
                        StringComparer.Ordinal);

                foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation is
                        {
                            Expression: MemberAccessExpressionSyntax
                            {
                                Name.Identifier.ValueText: "SetValues",
                                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CurrentValues", Expression: InvocationExpressionSyntax entryCall }
                            }
                        } &&
                        InvokedName(entryCall) == "Entry" &&
                        entryCall.ArgumentList.Arguments is [{ Expression: IdentifierNameSyntax setValuesTarget }] &&
                        declaredTypes.TryGetValue(setValuesTarget.Identifier.ValueText, out var setValuesType))
                        _fullReplaceCandidates.Add((Locate(path, invocation), Normalize(path), name!, setValuesType, setValuesTarget.Identifier.ValueText, "SetValues", node));
                    else if (InvokedName(invocation) == "Update" &&
                             invocation.ArgumentList.Arguments is [{ Expression: IdentifierNameSyntax updateTarget }] &&
                             declaredTypes.TryGetValue(updateTarget.Identifier.ValueText, out var updateType))
                        _fullReplaceCandidates.Add((Locate(path, invocation), Normalize(path), name!, updateType, updateTarget.Identifier.ValueText, "Update", node));
                }

                foreach (var assignment in node.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)))
                {
                    if (assignment.Left is IdentifierNameSyntax replacedTarget &&
                        declaredTypes.TryGetValue(replacedTarget.Identifier.ValueText, out var replacedType) &&
                        assignment.Right is BaseObjectCreationExpressionSyntax { Initializer: { } initializer })
                        _replacementCandidates.Add((Locate(path, assignment), Normalize(path), name!, replacedType, replacedTarget.Identifier.ValueText, initializer, node));
                }
            }
        }
        var constants = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => StringConstant(path, variable))
            .OfType<StringConstantSyntax>()
            .ToArray();
        // A constant that two types of one name define differently cannot be resolved by name, so it resolves to nothing.
        foreach (var constant in constants)
            _constants[constant.Key] = _constants.TryGetValue(constant.Key, out var existing) && existing != constant.Value ? null : constant.Value;
        _familyConstants.AddRange(constants
            .Where(constant => constant.Key.EndsWith(".SchemaFamily", StringComparison.Ordinal))
            .Select(constant => (constant.Location, constant.Key[..constant.Key.IndexOf('.')], constant.Value)));

        Checks.AddRange(root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation is
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method } access,
                ArgumentList.Arguments.Count: 2
            } && CheckMethods.Contains(method, StringComparer.Ordinal) && Rightmost(access.Expression) == "EfSchemaVersion")
            .Select(invocation => (Locate(path, invocation), invocation.ArgumentList.Arguments[0].Expression)));

        // A declaration's version is its last positional argument, so the shared, no-module two-argument form
        // (name, currentVersion) and the owned three-argument form (name, module, currentVersion) are both read.
        foreach (var attribute in root.AttributeLists
                     .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                     .SelectMany(list => list.Attributes)
                     .Where(attribute => DeclarationNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal)))
        {
            var arguments = attribute.ArgumentList?.Arguments ?? default;
            var positional = arguments.Where(argument => argument.NameEquals is null).ToArray();
            if (positional.Length < 2)
                continue;
            var upcasters = arguments
                .Where(argument => argument.NameEquals?.Name.Identifier.ValueText == "Upcasters")
                .SelectMany(argument => argument.Expression.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>())
                .Select(type => Rightmost(type.Type) ?? type.Type.ToString())
                .ToArray();
            Declarations.Add((Locate(path, attribute), positional[0].Expression, positional[^1].Expression, upcasters));
        }

        // A content declaration names its family, the entity type, then its columns; an integrity one a single column
        // and its reason.
        foreach (var attribute in root.AttributeLists
                     .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                     .SelectMany(list => list.Attributes)
                     .Where(attribute => ContentNames.Concat(IntegrityNames).Contains(Rightmost(attribute.Name), StringComparer.Ordinal)))
        {
            var integrity = IntegrityNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal);
            var positional = (attribute.ArgumentList?.Arguments ?? default).Where(argument => argument.NameEquals is null).Select(argument => argument.Expression).ToArray();
            var entity = positional.ElementAtOrDefault(1) is TypeOfExpressionSyntax typeOf ? Rightmost(typeOf.Type) ?? typeOf.Type.ToString() : null;
            var columns = integrity ? positional.Skip(2).Take(1).ToArray() : positional.Skip(2).ToArray();
            ColumnDeclarations.Add((Locate(path, attribute), Normalize(path), positional.ElementAtOrDefault(0), entity, columns, integrity, integrity ? positional.ElementAtOrDefault(3) : null));
        }

        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (type.BaseList?.Types.FirstOrDefault()?.Type is { } baseType)
                _baseTypes.TryAdd(type.Identifier.ValueText, Rightmost(baseType) ?? baseType.ToString());

            // A property's own declared type, so the restamp-completeness rule can resolve a row reached through a
            // chain of properties (`state.Entity`), not only a bare parameter or local (#2144).
            foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
                _propertyTypes.TryAdd((type.Identifier.ValueText, property.Identifier.ValueText), Rightmost(property.Type) ?? property.Type.ToString());

            var upcaster = type.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attribute => UpcasterNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal));
            if (upcaster is not null)
            {
                var arguments = upcaster.ArgumentList?.Arguments ?? default;
                _upcasters.Add((Locate(path, type), Normalize(path), type.Identifier.ValueText, arguments.Count == 2 ? arguments[0].Expression : null, arguments.Count == 2 ? arguments[1].Expression : null));
            }

            if (type.BaseList?.Types.Any(baseType => Rightmost(baseType.Type) == "IEfSchemaVersionedContext") == true)
            {
                var chain = type.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(property => property.Identifier.ValueText == "SchemaChain")?.ExpressionBody?.Expression;
                MaterializedFamilies.Add((Locate(path, type), type.Identifier.ValueText, chain is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Chain" } handle ? Rightmost(handle.Expression) : null));
            }

            if (!type.Modifiers.Any(SyntaxKind.AbstractKeyword) &&
                type.BaseList?.Types.FirstOrDefault(baseType => ProofBase(baseType.Type) is not null) is { } proof)
            {
                // The family is the base's first argument: a primary constructor's, or a constructor's ': base(...)'.
                var family = proof is PrimaryConstructorBaseTypeSyntax primary
                    ? primary.ArgumentList.Arguments.FirstOrDefault()?.Expression
                    : type.Members.OfType<ConstructorDeclarationSyntax>()
                        .Select(constructor => constructor.Initializer)
                        .FirstOrDefault(initializer => initializer?.IsKind(SyntaxKind.BaseConstructorInitializer) == true)?.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                Proofs.Add((Locate(path, type), Normalize(path), Rightmost(ProofBase(proof.Type)!.TypeArgumentList.Arguments[0]) ?? "", family));
            }

            // A stamped type declares a SchemaVersion member: the full-row-rewrite rule only judges a copy method
            // whose parameters share such a type, since an unstamped type has no stamp to leave behind.
            if (type.Members.Any(member => member switch
                {
                    PropertyDeclarationSyntax property => property.Identifier.ValueText == "SchemaVersion",
                    FieldDeclarationSyntax field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "SchemaVersion"),
                    _ => false
                }))
                _stampedTypes.Add(type.Identifier.ValueText);
        }

        Handles.AddRange(root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable is
            {
                Identifier.ValueText: "Chain",
                Initializer.Value: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Of" } of, ArgumentList.Arguments.Count: 2 }
            } && Rightmost(of.Expression) == "EfSchemaChain" && variable.Parent?.Parent is FieldDeclarationSyntax { Parent: TypeDeclarationSyntax })
            .Select(variable =>
            {
                var arguments = ((InvocationExpressionSyntax)variable.Initializer!.Value).ArgumentList.Arguments;
                var owner = ((TypeDeclarationSyntax)variable.Parent!.Parent!.Parent!).Identifier.ValueText;
                var assemblyOf = arguments[0].Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Assembly", Expression: TypeOfExpressionSyntax typeOf }
                    ? Rightmost(typeOf.Type)
                    : null;
                var family = arguments[1].Expression is IdentifierNameSyntax bare
                    ? SyntaxFactory.ParseExpression($"{owner}.{bare.Identifier.ValueText}")
                    : arguments[1].Expression;
                return (Locate(path, variable), owner, assemblyOf, family);
            }));

        // A column is known to be content only once every declaration is read, so every member write is kept.
        _memberWrites.AddRange(root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left is MemberAccessExpressionSyntax)
            .Select(assignment => (
                Locate(path, assignment),
                Normalize(path),
                ((MemberAccessExpressionSyntax)assignment.Left).Expression.ToString(),
                ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.ValueText,
                assignment.Ancestors().FirstOrDefault(node => node is MemberDeclarationSyntax or LocalFunctionStatementSyntax))));

        // A fresh row's SchemaVersion can be set inside its own object initializer rather than by a later dotted
        // assignment; the restamp-completeness rule holds that shape to the same bar (#2144). The row it creates is
        // named when the creation initializes a local (`var row = new Row {...}`) or replaces an assignment target
        // (`row = new Row {...}`, `state.Row = new Row {...}`); anything else - an argument, a return value - is
        // still judged on its initializer alone, since there is no row to look for a later write against.
        _stampedCreations.AddRange(root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(creation => creation.Initializer is not null &&
                creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                    .Any(assignment => assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "SchemaVersion" }))
            .Select(creation => (
                Locate(path, creation),
                Normalize(path),
                Member: creation.Ancestors().FirstOrDefault(node => node is MemberDeclarationSyntax or LocalFunctionStatementSyntax),
                Type: Rightmost(creation.Type) ?? creation.Type.ToString(),
                Row: creation.Parent switch
                {
                    EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } => variable.Identifier.ValueText,
                    AssignmentExpressionSyntax { Right: var right } assignment when right == creation && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) => assignment.Left.ToString(),
                    _ => (string?)null
                },
                creation.Initializer!,
                Node: (SyntaxNode)creation)));

        Stamps.AddRange(root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && Rightmost(assignment.Left) == "SchemaVersion")
            .Select(assignment => (Locate(path, assignment), assignment.Right)));
    }

    /// <summary>
    /// True when <paramref name="member"/> stamps <paramref name="row"/>: assigns its <c>SchemaVersion</c>, creates it
    /// with one, or passes it to a method of <paramref name="stamping"/> in the position that method stamps.
    /// </summary>
    private static bool StampsRow(SyntaxNode member, string row, IReadOnlySet<(string Name, int Arity, int Parameter)> stamping) =>
        member.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(stamp =>
            stamp.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SchemaVersion" } stamped && stamped.Expression.ToString() == row) ||
        member.DescendantNodes().OfType<VariableDeclaratorSyntax>().Any(variable =>
            variable.Identifier.ValueText == row &&
            variable.Initializer?.Value is BaseObjectCreationExpressionSyntax { Initializer: { } initializer } &&
            initializer.Expressions.OfType<AssignmentExpressionSyntax>().Any(stamp => Rightmost(stamp.Left) == "SchemaVersion")) ||
        member.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.ArgumentList.Arguments.Select((argument, index) => (argument, index)).Any(item =>
                item.argument.Expression.ToString() == row &&
                stamping.Contains((InvokedName(invocation), invocation.ArgumentList.Arguments.Count, item.index))));

    /// <summary>Grows a set of methods to a fixpoint: each pass adds what <paramref name="step"/> finds in any method.</summary>
    private IReadOnlySet<T> Fixpoint<T>(IEnumerable<T> seed, Func<(string Name, int Arity, string[] Parameters, SyntaxNode Node), IReadOnlySet<T>, IEnumerable<T>> step)
    {
        var known = seed.ToHashSet();
        while (_methods.SelectMany(method => step(method, known)).ToArray() is var found && found.Any(item => !known.Contains(item)))
            known.UnionWith(found);
        return known;
    }

    private static string InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        SimpleNameSyntax name => name.Identifier.ValueText,
        _ => ""
    };

    /// <summary>The member name <c>nameof(x.Member)</c> takes, or null when <paramref name="expression"/> is no <c>nameof</c>.</summary>
    private static string? NameOf(ExpressionSyntax? expression) =>
        expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [var argument] }
            ? Rightmost(argument.Expression) ?? (argument.Expression as IdentifierNameSyntax)?.Identifier.ValueText
            : null;

    private static bool IsWriteTarget(ExpressionSyntax expression) =>
        expression.Parent is AssignmentExpressionSyntax assignment && assignment.Left == expression;

    /// <summary>
    /// What consumes a read, so an exemption names the one use it excuses rather than every read in its member: the
    /// method or type it is an argument of, the operator it is an operand of, or the kind of node it sits in.
    /// </summary>
    private static string Consumer(ExpressionSyntax access) => access.Parent switch
    {
        ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation } => InvokedName(invocation) + "(...)",
        ArgumentSyntax { Parent.Parent: BaseObjectCreationExpressionSyntax creation } => creation is ObjectCreationExpressionSyntax named ? $"new {Rightmost(named.Type) ?? named.Type.ToString()}(...)" : "new(...)",
        BinaryExpressionSyntax binary => binary.OperatorToken.ValueText,
        AssignmentExpressionSyntax { Left: var target } => $"{target} =",
        var parent => parent?.Kind().ToString() ?? ""
    };

    /// <summary>
    /// True when <paramref name="access"/> goes through the chain: it is the value of a <c>(column, value)</c> argument of
    /// a chain's <c>Upcast</c>, or an argument of an upcasting helper.
    /// </summary>
    private bool IsChained(ExpressionSyntax access) =>
        access.Parent is ArgumentSyntax argument &&
        (argument.Parent is TupleExpressionSyntax { Arguments: [_, var value], Parent: ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax upcast } } &&
         value == argument && IsUpcasting(upcast) ||
         argument.Parent?.Parent is InvocationExpressionSyntax helper && UpcastingMethods.Contains((InvokedName(helper), helper.ArgumentList.Arguments.Count)));

    /// <summary>True when <paramref name="access"/> is copied, unread, into the same column of another row.</summary>
    private static bool IsCopy(ExpressionSyntax access) =>
        access.Parent is AssignmentExpressionSyntax { Left: var target } assignment && assignment.Right == access &&
        target switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == ColumnOf(access),
            IdentifierNameSyntax name => name.Identifier.ValueText == ColumnOf(access) && assignment.Parent is InitializerExpressionSyntax,
            _ => false
        };

    /// <summary>
    /// The column <paramref name="expression"/> reads: <c>row.Column</c>, or <c>row?.Column</c>, whose conditional access
    /// is read as a whole; anything else reads none.
    /// </summary>
    private static string? ColumnOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax binding } => binding.Name.Identifier.ValueText,
        _ => null
    };

    /// <summary>
    /// True when <paramref name="access"/> only names its column to EF: to the model builder, <c>Property(x =&gt; x.C)</c>,
    /// or as the target of a bulk update, <c>SetProperty(x =&gt; x.C, value)</c>, which the restamp rule holds instead.
    /// </summary>
    private static bool IsModelConfiguration(ExpressionSyntax access) =>
        access.Parent is LambdaExpressionSyntax { Parent: ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation } argument } &&
        (InvokedName(invocation) == "Property" || InvokedName(invocation) == "SetProperty" && invocation.ArgumentList.Arguments[0] == argument);

    private static bool InNameOf(ExpressionSyntax expression) =>
        expression.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation => NameOf(invocation) is not null);

    /// <summary>A declared column's name: its <c>nameof</c>, or the literal or constant it is written as.</summary>
    private string? ColumnName(ExpressionSyntax? expression) => NameOf(expression) ?? Resolve(expression);

    private static string MemberName(SyntaxNode node) =>
        node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            _ => ""
        };

    /// <summary>A chain handle names its family through its class's <c>SchemaFamily</c> constant: <c>Module.Chain</c>.</summary>
    private string? FamilyOfHandle(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Chain" } handle && Rightmost(handle.Expression) is { } owner
            ? _constants.GetValueOrDefault($"{owner}.SchemaFamily")
            : null;

    /// <summary>
    /// The materialization interceptor checks a context's chain, <c>context.SchemaChain</c>; the context's own family is
    /// held to its declaration by the materialized-family rule instead.
    /// </summary>
    private static bool IsContextChain(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SchemaChain" };

    /// <summary>A <c>const string</c> field of a type, initialized with a literal, keyed <c>Type.Field</c>.</summary>
    private static StringConstantSyntax? StringConstant(string path, VariableDeclaratorSyntax variable) =>
        variable is { Initializer.Value: LiteralExpressionSyntax literal, Parent.Parent: FieldDeclarationSyntax { Parent: TypeDeclarationSyntax type } field } &&
        literal.IsKind(SyntaxKind.StringLiteralExpression) &&
        field.Modifiers.Any(SyntaxKind.ConstKeyword)
            ? new StringConstantSyntax(Locate(path, variable), $"{type.Identifier.ValueText}.{variable.Identifier.ValueText}", literal.Token.ValueText)
            : null;

    /// <summary>The <c>EfSchemaUpcasterProof&lt;...&gt;</c> a base type names, however qualified, or null.</summary>
    private static GenericNameSyntax? ProofBase(TypeSyntax type) => type switch
    {
        GenericNameSyntax { Identifier.ValueText: "EfSchemaUpcasterProof", TypeArgumentList.Arguments.Count: > 0 } generic => generic,
        QualifiedNameSyntax { Right: GenericNameSyntax right } => ProofBase(right),
        _ => null
    };

    private static string Normalize(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    public string? Resolve(ExpressionSyntax? expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        MemberAccessExpressionSyntax access => _constants.GetValueOrDefault(Key(access) ?? ""),
        _ => null
    };

    private static string? Key(ExpressionSyntax? expression) =>
        expression is MemberAccessExpressionSyntax access && Rightmost(access.Expression) is { } owner ? $"{owner}.{access.Name.Identifier.ValueText}" : null;

    private static string? Rightmost(SyntaxNode node) => node switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        _ => null
    };

    private static IReadOnlyList<string> Ordered(IEnumerable<string> violations) => violations.Order(StringComparer.Ordinal).ToArray();

    private static string Locate(string path, SyntaxNode node) =>
        $"{path.Replace(Path.DirectorySeparatorChar, '/')}({node.GetLocation().GetLineSpan().StartLinePosition.Line + 1})";

    private sealed record StringConstantSyntax(string Location, string Key, string Value);
}

/// <summary>
/// Which source can see which declaration: a source sees an entity's content declaration when its project is the
/// declaring project or references it, directly or through other projects, since only then can it name the entity at
/// all. The read rule matches columns by name, so this keeps a name one family declares, such as Secrets'
/// <c>Payload</c>, from judging an unrelated member of the same name in a project that cannot see that family.
/// </summary>
internal static class ProjectVisibility
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Owners = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlySet<string>> Closures = new(StringComparer.Ordinal);
    private static readonly System.Text.RegularExpressions.Regex ProjectReference = new(@"<ProjectReference\s+Include=""(?<path>[^""]+)""");

    public static bool Sees(string reader, string declaration)
    {
        var declaring = Owner(declaration);
        var project = Owner(reader);
        return project == declaring || Closure(project).Contains(declaring);
    }

    /// <summary>The project file that owns <paramref name="path"/>: the nearest one in its directory or above.</summary>
    private static string Owner(string path) => Owners.GetOrAdd(path, relative =>
    {
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(Path.Join(RepoPaths.RepoRoot, relative))); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.EnumerateFiles(directory, "*.csproj").FirstOrDefault() is { } project)
                return project;
            if (string.Equals(directory, Path.GetFullPath(RepoPaths.RepoRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                break;
        }

        throw new InvalidOperationException($"'{relative}' belongs to no project, so the read rule cannot tell what it sees.");
    });

    /// <summary>Every project <paramref name="project"/> references, directly or transitively.</summary>
    private static IReadOnlySet<string> Closure(string project) => Closures.GetOrAdd(project, root =>
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var current))
        {
            foreach (System.Text.RegularExpressions.Match reference in ProjectReference.Matches(File.ReadAllText(current)))
            {
                var referenced = Path.GetFullPath(Path.Join(Path.GetDirectoryName(current)!, reference.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (File.Exists(referenced) && seen.Add(referenced))
                    pending.Push(referenced);
            }
        }

        return seen;
    });
}

/// <summary>
/// FR-022's fixture rules over a set of committed fixtures, their lock and the declared upcasters, so the detector can
/// be pinned on fixtures that never touch the tree. A fixture is one row of one table, every content column in one JSON
/// file named by the table (#2144), so a name with a column in it - the per-column layout upcasters had before they
/// worked on rows - is refused.
/// </summary>
internal static class UpcasterFixtureRules
{
    private static readonly System.Text.RegularExpressions.Regex Named = new(
        @"/Fixtures/SchemaUpcasters/(?<family>[^/]+)/(?<from>[^/]+)-to-(?<to>[^/]+)/(?<name>[^/.]+)\.(?<role>source|expected)\.(?<ext>json)$");

    public static IReadOnlyList<string> Violations(
        IReadOnlyList<(string Path, string Text)> fixtures,
        string lockText,
        IEnumerable<(string Location, string Family, string From, string To)> upcasters)
    {
        var violations = new List<string>();
        var named = fixtures.Select(fixture => (fixture.Path, fixture.Text, Match: Named.Match("/" + fixture.Path))).ToArray();
        violations.AddRange(named.Where(fixture => !fixture.Match.Success)
            .Select(fixture => $"{fixture.Path}: is not named <table>.source.json or <table>.expected.json under Fixtures/SchemaUpcasters/<family>/<from>-to-<to>/; " +
                               "a fixture is one row, every content column of the table in one file (#2144)."));

        var pairs = named.Where(fixture => fixture.Match.Success)
            .GroupBy(fixture => fixture.Path[..(fixture.Path.Length - fixture.Match.Groups["role"].Length - fixture.Match.Groups["ext"].Length - 1)], StringComparer.Ordinal)
            .ToArray();
        foreach (var pair in pairs)
        {
            var roles = pair.Select(fixture => fixture.Match.Groups["role"].Value).ToHashSet(StringComparer.Ordinal);
            if (!roles.Contains("expected"))
                violations.Add($"{pair.Key}source: has no matching expected fixture.");
            if (!roles.Contains("source"))
                violations.Add($"{pair.Key}expected: has no matching source fixture.");
        }

        foreach (var (location, family, from, to) in upcasters)
        {
            var complete = pairs.Any(pair => pair.Count() == 2 && pair.All(fixture =>
                fixture.Match.Groups["family"].Value == family && fixture.Match.Groups["from"].Value == from && fixture.Match.Groups["to"].Value == to));
            if (!complete)
                violations.Add($"{location}: the '{family}' upcaster from '{from}' to '{to}' ships no fixture pair under Fixtures/SchemaUpcasters/{family}/{from}-to-{to}/.");
        }

        var recorded = lockText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split("  ", 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        foreach (var (path, text) in fixtures)
        {
            if (!recorded.TryGetValue(path, out var hash))
                violations.Add($"{path}: is not recorded in the fixture lock; record '{Hash(text)}  {path}' once its version ships.");
            else if (!StringComparer.Ordinal.Equals(hash, Hash(text)))
                violations.Add($"{path}: was edited after it was recorded; a shipped fixture is frozen and never regenerated.");
        }

        violations.AddRange(recorded.Keys.Where(path => fixtures.All(fixture => fixture.Path != path))
            .Select(path => $"{path}: was deleted; a fixture stays committed even after its upcaster retires (FR-024)."));
        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>The fixture pairs the committed fixtures form: each step directory a family's fixtures sit in.</summary>
    public static IReadOnlyList<(string Directory, string Family, string From, string To)> Pairs(IEnumerable<string> paths) =>
        paths.Select(path => (Path: path, Match: Named.Match("/" + path)))
            .Where(fixture => fixture.Match.Success)
            .Select(fixture => (
                Directory: fixture.Path[..fixture.Path.LastIndexOf('/')],
                Family: fixture.Match.Groups["family"].Value,
                From: fixture.Match.Groups["from"].Value,
                To: fixture.Match.Groups["to"].Value))
            .Distinct()
            .ToArray();

    /// <summary>SHA-256 of the fixture with line endings normalized, so a checkout's line-ending conversion is no edit.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
}

/// <summary>
/// FR-022's proof rules over the shipped upcasters, the proof classes, every upcaster type seen and the committed
/// fixture pairs, so the detector can be pinned on sets that never touch the tree. A proof is a concrete class
/// deriving directly from <c>EfSchemaUpcasterProof&lt;TUpcaster, TValue&gt;(family, store)</c>, whose three proofs
/// are fixed in that base class: naming the upcaster and its family is all a module can do, so it cannot ship one
/// proof without the other two.
/// </summary>
internal static class UpcasterProofRules
{
    public static IReadOnlyList<string> Violations(
        IEnumerable<(string Location, string Family, string Upcaster, string From, string To)> shipped,
        IReadOnlyList<(string Location, string Path, string Upcaster, string? Family)> proofs,
        IReadOnlyList<(string Path, string Class, string? From, string? To)> upcasters,
        IEnumerable<(string Directory, string Family, string From, string To)> fixturePairs)
    {
        var violations = new List<string>();
        violations.AddRange(proofs.Where(proof => proof.Family is null)
            .Select(proof => $"{proof.Location}: proves '{proof.Upcaster}' for a family this guard cannot resolve; name the family with a literal or a constant."));

        var proven = proofs.Where(proof => proof.Family is not null)
            .Select(proof => (proof.Location, proof.Upcaster, Family: proof.Family!, Step: StepOf(proof.Path, proof.Upcaster, upcasters)))
            .ToArray();
        violations.AddRange(proven.Where(proof => proof.Step is null)
            .Select(proof => $"{proof.Location}: proves '{proof.Upcaster}', which this guard cannot resolve to one type carrying [EfSchemaUpcaster(from, to)]."));
        violations.AddRange(shipped
            .Where(step => !proven.Any(proof => proof.Upcaster == step.Upcaster && proof.Family == step.Family))
            .Select(step => $"{step.Location}: the '{step.Family}' upcaster '{step.Upcaster}' from '{step.From}' to '{step.To}' ships without " +
                            $"FR-022's proofs; derive a test class from EfSchemaUpcasterProof<{step.Upcaster}, ...>(\"{step.Family}\", ...)."));
        violations.AddRange(fixturePairs
            .Where(pair => !proven.Any(proof => proof.Family == pair.Family && proof.Step == (pair.From, pair.To)))
            .Select(pair => $"{pair.Directory}: no EfSchemaUpcasterProof proves the '{pair.Family}' fixture pair from '{pair.From}' to '{pair.To}', " +
                            "so nothing checks its upcast, its old-format round trip or its read through the store."));
        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>The step of the upcaster a proof names: the one in the proof's own file, else the only one by that name.</summary>
    private static (string From, string To)? StepOf(string path, string upcaster, IReadOnlyList<(string Path, string Class, string? From, string? To)> upcasters)
    {
        var named = upcasters.Where(candidate => candidate.Class == upcaster).ToArray();
        var local = named.Where(candidate => candidate.Path == path).ToArray();
        var match = local.Length == 1 ? local[0] : named.Length == 1 ? named[0] : default;
        return match is { From: { } from, To: { } to } ? (from, to) : null;
    }
}
