using System.Collections.Immutable;
using System.Globalization;
using Elsa.Expressions.Core.Models;
using Fluid;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>
/// Immutable, module-owned description of the Liquid syntax and filters used by one binding profile.
/// It stores registration recipes and metadata, never parser or template-options instances.
/// </summary>
public sealed class LiquidExpressionProfile
{
    public const string DefaultRevision = "liquid-7c9f1e2a4b6d";

    private static readonly ImmutableHashSet<string> DisabledFilters = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase, "date", "format_date", "time_zone");

    private static readonly ImmutableHashSet<string> DisabledTags = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase, "include", "render", "macro", "from");

    private readonly Action<FluidParser> configureParser;
    private readonly Action<TemplateOptions> configureTemplateOptions;

    public static LiquidExpressionProfile Default { get; } = CreateDefault();

    public ExpressionToolingCatalog ToolingCatalog { get; }

    private LiquidExpressionProfile(
        string revision,
        IReadOnlyList<ExpressionSymbol> symbols,
        Action<FluidParser>? configureParser,
        Action<TemplateOptions>? configureTemplateOptions)
    {
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("A Liquid profile revision is required.", nameof(revision));

        ArgumentNullException.ThrowIfNull(symbols);
        var frozenSymbols = symbols.Select(Freeze).ToImmutableArray();
        ValidateSymbols(frozenSymbols);

        ToolingCatalog = new ExpressionToolingCatalog(revision, frozenSymbols);
        this.configureParser = configureParser ?? (static _ => { });
        this.configureTemplateOptions = configureTemplateOptions ?? (static _ => { });
    }

    /// <summary>
    /// Creates a trusted profile for host extensions. Supply the complete effective tag/filter catalog
    /// and stateless recipes that configure fresh Fluid objects; the Core catalog contains metadata only.
    /// </summary>
    public static LiquidExpressionProfile Create(
        string revision,
        IReadOnlyList<ExpressionSymbol> symbols,
        Action<FluidParser>? configureParser = null,
        Action<TemplateOptions>? configureTemplateOptions = null) =>
        new(revision, symbols, configureParser, configureTemplateOptions);

    /// <summary>Creates a new parser with this profile's parser options and registrations.</summary>
    public FluidParser CreateParser()
    {
        var parser = new FluidParser(new FluidParserOptions { AllowFunctions = false });
        ConfigureParser(parser);
        return parser;
    }

    /// <summary>Applies this profile to a parser supplied through the legacy scoped-parser seam.</summary>
    public void ConfigureParser(FluidParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        configureParser(parser);

        // These tags resolve files through Fluid's template/file surface and are outside pure bindings.
        foreach (var tag in DisabledTags)
            parser.RegisteredTags.Remove(tag);
    }

    /// <summary>Creates fresh per-evaluation options for the binding-pure runtime.</summary>
    public TemplateOptions CreateTemplateOptions()
    {
        var options = new TemplateOptions();
        configureTemplateOptions(options);

        options.Now = static () => DateTimeOffset.UnixEpoch;
        options.TimeZone = TimeZoneInfo.Utc;
        options.CultureInfo = CultureInfo.InvariantCulture;
        options.FileProvider = null;
        foreach (var filter in DisabledFilters)
        {
            options.Filters.AddFilter(filter, static (_, _, _) =>
                throw new InvalidOperationException("Liquid time and time-zone filters are unavailable in the binding-pure-v1 capability profile."));
        }

        return options;
    }

    private static LiquidExpressionProfile CreateDefault()
    {
        var parser = CreateDefaultParser();

        var filters = new TemplateOptions().Filters
            .Select(filter => filter.Key)
            .Where(name => !DisabledFilters.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(CreateFilterSymbol);
        var tags = parser.RegisteredTags.Keys
            .Where(name => name != "#" && !DisabledTags.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(CreateTagSymbol);

        return new(DefaultRevision, filters.Concat(tags).ToArray(), null, null);
    }

    private static FluidParser CreateDefaultParser() =>
        new(new FluidParserOptions { AllowFunctions = false });

    private static ExpressionSymbol CreateFilterSymbol(string name) => name switch
    {
        "append" => new(
            $"liquid:filter:{name}", name, ExpressionSymbolKind.Filter,
            Documentation: "Appends the argument to the input text.",
            Signatures: [Signature("append(value): String", ["value"], StringShape())]),
        "upcase" => new(
            $"liquid:filter:{name}", name, ExpressionSymbolKind.Filter,
            Documentation: "Converts the input text to uppercase.",
            Signatures: [Signature("upcase(): String", [], StringShape())]),
        _ => new(
            $"liquid:filter:{name}", name, ExpressionSymbolKind.Filter,
            Documentation: $"Fluid 2.31.0 built-in filter in the binding-pure profile: {name}.")
    };

    private static ExpressionSymbol CreateTagSymbol(string name) => name switch
    {
        "for" => new(
            $"liquid:tag:{name}", name, ExpressionSymbolKind.Tag,
            Documentation: "Renders the block once for each item in a collection.",
            Signatures: [Signature("for item in collection", ["item", "collection"])]),
        "if" => new(
            $"liquid:tag:{name}", name, ExpressionSymbolKind.Tag,
            Documentation: "Renders the block when its condition is true.",
            Signatures: [Signature("if condition", ["condition"])]),
        _ => new(
            $"liquid:tag:{name}", name, ExpressionSymbolKind.Tag,
            Documentation: $"Fluid 2.31.0 built-in tag in the binding-pure profile: {name}.")
    };

    private static ExpressionCallableSignature Signature(
        string display,
        IReadOnlyList<string> parameters,
        ExpressionValueShape? returnShape = null) =>
        new(display, parameters.ToImmutableArray(), returnShape);

    private static ExpressionValueShape StringShape() =>
        new("String", ExpressionValueKind.Scalar, IsNullable: false);

    private static void ValidateSymbols(IReadOnlyList<ExpressionSymbol> symbols)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol.SymbolId) || string.IsNullOrWhiteSpace(symbol.Name))
                throw new ArgumentException("Liquid profile symbols require stable IDs and names.", nameof(symbols));
            if (symbol.Kind is not (ExpressionSymbolKind.Filter or ExpressionSymbolKind.Tag))
                throw new ArgumentException("Liquid profile catalogs may contain only tags and filters.", nameof(symbols));
            if (!ids.Add(symbol.SymbolId))
                throw new ArgumentException($"Liquid profile symbol ID '{symbol.SymbolId}' is duplicated.", nameof(symbols));
            if ((symbol.Kind == ExpressionSymbolKind.Filter && DisabledFilters.Contains(symbol.Name)) ||
                (symbol.Kind == ExpressionSymbolKind.Tag && DisabledTags.Contains(symbol.Name)))
                throw new ArgumentException($"'{symbol.Name}' is unavailable in the binding-pure Liquid profile.", nameof(symbols));
        }
    }

    private static ExpressionSymbol Freeze(ExpressionSymbol symbol) => symbol with
    {
        ValueShape = Freeze(symbol.ValueShape),
        Signatures = symbol.Signatures?.Select(signature => signature with
        {
            Parameters = signature.Parameters?.ToImmutableArray(),
            ReturnShape = Freeze(signature.ReturnShape)
        }).ToImmutableArray()
    };

    private static ExpressionValueShape? Freeze(ExpressionValueShape? shape) => shape is null
        ? null
        : shape with
        {
            Members = shape.Members?.Select(member => member with { Shape = Freeze(member.Shape)! }).ToImmutableArray(),
            Item = Freeze(shape.Item),
            Key = Freeze(shape.Key),
            Value = Freeze(shape.Value)
        };
}
