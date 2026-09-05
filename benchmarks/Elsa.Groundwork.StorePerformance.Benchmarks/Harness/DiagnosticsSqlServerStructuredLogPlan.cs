using System.Text.RegularExpressions;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public static partial class DiagnosticsNativePlanContract
{
    private const string StructuredLogsSqlServerPrimaryKey = "__groundwork_pk_elsa_structured_logs";

    internal static bool IsSqlServerStructuredLogPrimaryKeyRoute(
        string provider,
        string adapter,
        DiagnosticsNativeRouteSpec specification) =>
        string.Equals(provider, "sqlserver", StringComparison.Ordinal) &&
        string.Equals(adapter, GroundworkAdapter, StringComparison.Ordinal) &&
        specification.RouteIdentity is "structured-log-recent" or "structured-log-replay" &&
        string.Equals(specification.TableName, "elsa_structured_logs", StringComparison.Ordinal) &&
        string.Equals(specification.IndexName, "elsa_structured_logs_sequence_order", StringComparison.Ordinal) &&
        string.Equals(specification.OrderColumn, "sequence", StringComparison.Ordinal) &&
        specification.PredicateColumn is null &&
        specification.StorageScopeRequired &&
        specification.PhysicalCardinality == DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream &&
        specification.FiniteLimit == DiagnosticsDurableHistoryWorkload.QueryLimit &&
        specification.EffectiveOrdering.Count == 1 &&
        string.Equals(specification.EffectiveOrdering[0].Column, "sequence", StringComparison.Ordinal) &&
        ((specification.RouteIdentity == "structured-log-recent" &&
          specification.EffectiveOrdering[0].Direction == RuntimeNativeOrderDirection.Descending) ||
         (specification.RouteIdentity == "structured-log-replay" &&
          specification.EffectiveOrdering[0].Direction == RuntimeNativeOrderDirection.Ascending)) &&
        specification.Descending == (specification.RouteIdentity == "structured-log-recent") &&
        specification.NullableOrderingColumns is { Count: 0 };

    internal static bool TryResolveSqlServerStructuredLogPrimaryKey(
        string provider,
        string adapter,
        DiagnosticsNativeRouteSpec specification,
        string command,
        string nativePlan,
        out string physicalIndexName)
    {
        physicalIndexName = string.Empty;
        if (!IsSqlServerStructuredLogPrimaryKeyRoute(provider, adapter, specification))
            return false;

        try
        {
            ValidateSqlCommand(provider, command, specification);
            var document = System.Xml.Linq.XDocument.Parse(nativePlan, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            if (!IsSqlServerStructuredLogPrimaryKeyPlan(document, command, specification))
                return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (PerformanceContractException)
        {
            return false;
        }

        physicalIndexName = StructuredLogsSqlServerPrimaryKey;
        return true;
    }

    /// <summary>
    /// SQL Server may satisfy the structured-log recent or replay query from the declared primary key
    /// rather than the redundant secondary index. This is an equivalence proof for those two
    /// frozen Groundwork routes, not a general "any index" escape hatch: the retained command, seek,
    /// direction, runtime fetch, row, lookup, and no-spill facts must all agree.
    /// </summary>
    private static bool IsSqlServerStructuredLogPrimaryKeyPlan(
        System.Xml.Linq.XDocument document,
        string command,
        DiagnosticsNativeRouteSpec specification)
    {
        var relops = document.Descendants().Where(element => element.Name.LocalName == "RelOp").ToArray();
        if (relops.Any(IsSqlServerSortOrMaterialization) ||
            document.Descendants().Any(element => element.Name.LocalName.Contains("Sort", StringComparison.OrdinalIgnoreCase) ||
                                                   element.Name.LocalName.Contains("Spool", StringComparison.OrdinalIgnoreCase) ||
                                                   element.Name.LocalName.Contains("Material", StringComparison.OrdinalIgnoreCase)) ||
            document.Descendants().Any(element => element.Name.LocalName is "SpillOccurred" or "SpillWarning" or "SpillToTempDb") ||
            document.Descendants().SelectMany(element => element.Attributes()).Any(attribute =>
                attribute.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase) && IsPositiveFlag(attribute.Value)) ||
            relops.Any(element => element.Attribute("PhysicalOp")?.Value.Contains("Scan", StringComparison.OrdinalIgnoreCase) == true))
            return false;

        var seeks = relops.Where(element => string.Equals(element.Attribute("PhysicalOp")?.Value, "Index Seek", StringComparison.Ordinal)).ToArray();
        var tops = relops.Where(element => string.Equals(element.Attribute("PhysicalOp")?.Value, "Top", StringComparison.Ordinal)).ToArray();
        if (seeks.Length != 1 || tops.Length != 1)
            return false;

        if (relops.Length != 5 ||
            relops.Count(element => element.Attribute("PhysicalOp")?.Value == "Nested Loops") != 1 ||
            relops.Count(element => element.Attribute("PhysicalOp")?.Value == "Filter") != 1 ||
            relops.Count(element => element.Attribute("PhysicalOp")?.Value == "RID Lookup") != 1)
            return false;

        var seek = seeks[0];
        var nestedLoops = relops.Single(element => element.Attribute("PhysicalOp")?.Value == "Nested Loops");
        var filter = relops.Single(element => element.Attribute("PhysicalOp")?.Value == "Filter");
        var lookup = relops.Single(element => element.Attribute("PhysicalOp")?.Value == "RID Lookup");
        if (!nestedLoops.Ancestors().Contains(tops[0]) ||
            !filter.Ancestors().Contains(nestedLoops) ||
            !seek.Ancestors().Contains(filter) ||
            !lookup.Ancestors().Contains(nestedLoops))
            return false;
        if (!SqlServerStructuredLogFilterPredicateMatches(filter, command, specification))
            return false;

        var indexScans = seek.Descendants().Where(element => element.Name.LocalName == "IndexScan").ToArray();
        if (indexScans.Length != 1 || !seek.Ancestors().Contains(tops[0]))
            return false;
        var indexScan = indexScans[0];
        var objects = indexScan.Descendants().Where(element => element.Name.LocalName == "Object").ToArray();
        if (objects.Length != 1 ||
            !string.Equals(objects[0].Attribute("Table")?.Value.Trim('[', ']'), specification.TableName, StringComparison.Ordinal) ||
            !string.Equals(objects[0].Attribute("Index")?.Value.Trim('[', ']'), StructuredLogsSqlServerPrimaryKey, StringComparison.Ordinal))
            return false;

        var expectedDirection = specification.RouteIdentity == "structured-log-recent" ? "BACKWARD" : "FORWARD";
        if (!string.Equals(indexScan.Attribute("Ordered")?.Value, "1", StringComparison.Ordinal) ||
            !string.Equals(indexScan.Attribute("ScanDirection")?.Value, expectedDirection, StringComparison.Ordinal) ||
            !string.Equals(indexScan.Attribute("ForcedIndex")?.Value, "0", StringComparison.Ordinal) ||
            !string.Equals(indexScan.Attribute("ForceSeek")?.Value, "0", StringComparison.Ordinal) ||
            !string.Equals(indexScan.Attribute("ForceScan")?.Value, "0", StringComparison.Ordinal) ||
            indexScan.Attribute("NoExpandHint") is { Value: not "0" } ||
            !SqlServerStructuredLogSeekMatches(indexScan, command, specification))
            return false;

        var expectedFetch = ExpectedNativeFetchLimit(specification);
        if (!SqlServerTopFetchMatches(document, tops[0], command, specification, expectedFetch) ||
            !TrySqlServerRuntimeCount(tops[0], "ActualRows", out var topRows) ||
            topRows < specification.FiniteLimit ||
            topRows > expectedFetch ||
            !TrySqlServerRuntimeCount(seek, "ActualRows", out var seekRows) ||
            !TrySqlServerRuntimeCount(seek, "ActualRowsRead", out var seekRowsRead) ||
            seekRows < topRows ||
            seekRows > seekRowsRead ||
            seekRowsRead > expectedFetch)
            return false;

        var lookupIndexScans = lookup.Descendants().Where(element => element.Name.LocalName == "IndexScan").ToArray();
        var lookupObjects = lookup.Descendants().Where(element => element.Name.LocalName == "Object").ToArray();
        if (lookupIndexScans.Length != 1 ||
            lookupObjects.Length != 1 ||
            !string.Equals(lookupObjects[0].Attribute("Table")?.Value.Trim('[', ']'), specification.TableName, StringComparison.Ordinal) ||
            !string.Equals(lookupIndexScans[0].Attribute("Lookup")?.Value, "1", StringComparison.Ordinal) ||
            !string.Equals(lookupIndexScans[0].Attribute("Ordered")?.Value, "1", StringComparison.Ordinal) ||
            !string.Equals(lookupIndexScans[0].Attribute("ScanDirection")?.Value, "FORWARD", StringComparison.Ordinal) ||
            !string.Equals(lookupIndexScans[0].Attribute("ForcedIndex")?.Value, "0", StringComparison.Ordinal) ||
            !string.Equals(lookupIndexScans[0].Attribute("ForceSeek")?.Value, "0", StringComparison.Ordinal) ||
            !string.Equals(lookupIndexScans[0].Attribute("ForceScan")?.Value, "0", StringComparison.Ordinal) ||
            lookupIndexScans[0].Attribute("NoExpandHint") is { Value: not "0" } ||
            !SqlServerRidLookupBookmarkMatches(seek, nestedLoops, lookup) ||
            !TrySqlServerRuntimeCount(lookup, "ActualExecutions", out var lookupExecutions) ||
            !TrySqlServerRuntimeCount(lookup, "ActualRows", out var lookupRows) ||
            !TrySqlServerRuntimeCount(lookup, "ActualRowsRead", out var lookupRowsRead) ||
            lookupExecutions > expectedFetch ||
            lookupExecutions < topRows ||
            lookupRows < topRows ||
            lookupRows > lookupRowsRead ||
            lookupRowsRead > expectedFetch)
            return false;

        return true;
    }

    private static bool SqlServerRidLookupBookmarkMatches(
        System.Xml.Linq.XElement seek,
        System.Xml.Linq.XElement nestedLoops,
        System.Xml.Linq.XElement lookup)
    {
        var loopOperators = nestedLoops.Elements().Where(element => element.Name.LocalName == "NestedLoops").ToArray();
        if (loopOperators.Length != 1)
            return false;

        var outerReferences = loopOperators[0].Elements().Where(element => element.Name.LocalName == "OuterReferences").ToArray();
        if (outerReferences.Length != 1)
            return false;

        var outerColumns = outerReferences[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        if (outerColumns.Length == 0 || outerReferences[0].Elements().Any(element => element.Name.LocalName != "ColumnReference") ||
            outerColumns.Any(column => column.Elements().Any()))
            return false;

        var seekOutputs = seek.Elements().Where(element => element.Name.LocalName == "OutputList").ToArray();
        if (seekOutputs.Length != 1)
            return false;

        var seekBookmarks = seekOutputs[0].Elements().Where(element => element.Name.LocalName == "ColumnReference" &&
            (element.Attribute("Column")?.Value.StartsWith("Bmk", StringComparison.Ordinal) ?? false)).ToArray();
        if (seekBookmarks.Length != 1 || seekOutputs[0].Elements().Any(element => element.Name.LocalName != "ColumnReference"))
            return false;

        var bookmark = seekBookmarks[0].Attribute("Column")?.Value;
        if (bookmark is null || outerColumns.Count(column => string.Equals(column.Attribute("Column")?.Value, bookmark, StringComparison.Ordinal)) != 1)
            return false;

        var lookupSeekKeys = lookup.Descendants().Where(element => element.Name.LocalName == "SeekKeys").ToArray();
        if (lookupSeekKeys.Length != 1)
            return false;
        var prefixes = lookupSeekKeys[0].Elements().Where(element => element.Name.LocalName == "Prefix").ToArray();
        if (prefixes.Length != 1 || lookupSeekKeys[0].Elements().Any(element => element.Name.LocalName != "Prefix"))
            return false;

        var prefix = prefixes[0];
        if (!string.Equals(prefix.Attribute("ScanType")?.Value, "EQ", StringComparison.Ordinal) ||
            prefix.Elements().Any(element => element.Name.LocalName is not ("RangeColumns" or "RangeExpressions")))
            return false;

        var rangeColumns = prefix.Elements().Where(element => element.Name.LocalName == "RangeColumns").ToArray();
        var rangeExpressions = prefix.Elements().Where(element => element.Name.LocalName == "RangeExpressions").ToArray();
        if (rangeColumns.Length != 1 || rangeExpressions.Length != 1)
            return false;

        var rangeColumnReferences = rangeColumns[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        if (rangeColumnReferences.Length != 1 || rangeColumns[0].Elements().Any(element => element.Name.LocalName != "ColumnReference") ||
            !string.Equals(rangeColumnReferences[0].Attribute("Column")?.Value, bookmark, StringComparison.Ordinal) ||
            rangeColumnReferences[0].Elements().Any())
            return false;

        var scalarOperators = rangeExpressions[0].Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        if (scalarOperators.Length != 1 || rangeExpressions[0].Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
            return false;
        var identifiers = scalarOperators[0].Elements().Where(element => element.Name.LocalName == "Identifier").ToArray();
        if (identifiers.Length != 1 || scalarOperators[0].Elements().Any(element => element.Name.LocalName != "Identifier"))
            return false;
        var expressionReferences = identifiers[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        return expressionReferences.Length == 1 &&
               identifiers[0].Elements().All(element => element.Name.LocalName == "ColumnReference") &&
               string.Equals(expressionReferences[0].Attribute("Column")?.Value, bookmark, StringComparison.Ordinal) &&
               !expressionReferences[0].Elements().Any();
    }

    private static bool SqlServerStructuredLogFilterPredicateMatches(
        System.Xml.Linq.XElement filter,
        string command,
        DiagnosticsNativeRouteSpec specification)
    {
        var filters = filter.Elements().Where(element => element.Name.LocalName == "Filter").ToArray();
        if (filters.Length != 1)
            return false;

        var predicates = filters[0].Elements().Where(element => element.Name.LocalName == "Predicate").ToArray();
        if (predicates.Length != 1)
            return false;

        var predicate = predicates[0];
        var predicateOperators = predicate.Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        if (predicateOperators.Length != 1 || predicate.Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
            return false;

        var compares = predicateOperators[0].Elements().Where(element => element.Name.LocalName == "Compare").ToArray();
        if (compares.Length != 1 || predicateOperators[0].Elements().Any(element => element.Name.LocalName != "Compare") ||
            !string.Equals(compares[0].Attribute("CompareOp")?.Value, "EQ", StringComparison.Ordinal))
            return false;

        var operands = compares[0].Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        if (operands.Length != 2 || compares[0].Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
            return false;

        var parameter = SqlServerCommandParameter(command, "__groundwork_scope", "=");
        return parameter is not null &&
               SqlServerDatalengthColumnOperandMatches(operands[0], specification.TableName) &&
               SqlServerDatalengthParameterOperandMatches(operands[1], parameter);
    }

    private static bool SqlServerDatalengthColumnOperandMatches(
        System.Xml.Linq.XElement operand,
        string expectedTable)
    {
        var intrinsics = operand.Elements().Where(element => element.Name.LocalName == "Intrinsic").ToArray();
        if (intrinsics.Length != 1 || operand.Elements().Any(element => element.Name.LocalName != "Intrinsic") ||
            !string.Equals(intrinsics[0].Attribute("FunctionName")?.Value, "datalength", StringComparison.OrdinalIgnoreCase))
            return false;

        var innerOperators = intrinsics[0].Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        if (innerOperators.Length != 1 || intrinsics[0].Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
            return false;

        var identifiers = innerOperators[0].Elements().Where(element => element.Name.LocalName == "Identifier").ToArray();
        if (identifiers.Length != 1 || innerOperators[0].Elements().Any(element => element.Name.LocalName != "Identifier"))
            return false;

        var references = identifiers[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        return references.Length == 1 &&
               identifiers[0].Elements().All(element => element.Name.LocalName == "ColumnReference") &&
               string.Equals(references[0].Attribute("Column")?.Value, "__groundwork_scope", StringComparison.Ordinal) &&
               string.Equals(references[0].Attribute("Table")?.Value.Trim('[', ']'), expectedTable, StringComparison.Ordinal) &&
               !references[0].Elements().Any();
    }

    private static bool SqlServerDatalengthParameterOperandMatches(
        System.Xml.Linq.XElement operand,
        string parameter)
    {
        var identifiers = operand.Elements().Where(element => element.Name.LocalName == "Identifier").ToArray();
        if (identifiers.Length == 1 && operand.Elements().All(element => element.Name.LocalName == "Identifier"))
        {
            var references = identifiers[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
            if (references.Length != 1 || identifiers[0].Elements().Any(element => element.Name.LocalName != "ColumnReference") ||
                !Regex.IsMatch(references[0].Attribute("Column")?.Value ?? string.Empty, "^ConstExpr[0-9]+$", RegexOptions.CultureInvariant))
                return false;

            var nestedOperators = references[0].Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
            if (nestedOperators.Length != 1 || references[0].Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
                return false;
            operand = nestedOperators[0];
        }

        var intrinsics = operand.Elements().Where(element => element.Name.LocalName == "Intrinsic").ToArray();
        if (intrinsics.Length != 1 || operand.Elements().Any(element => element.Name.LocalName != "Intrinsic") ||
            !string.Equals(intrinsics[0].Attribute("FunctionName")?.Value, "datalength", StringComparison.OrdinalIgnoreCase))
            return false;

        var innerOperators = intrinsics[0].Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        if (innerOperators.Length != 1 || intrinsics[0].Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
            return false;

        var parameterIdentifiers = innerOperators[0].Elements().Where(element => element.Name.LocalName == "Identifier").ToArray();
        if (parameterIdentifiers.Length != 1 || innerOperators[0].Elements().Any(element => element.Name.LocalName != "Identifier"))
            return false;

        var parameterReferences = parameterIdentifiers[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        return parameterReferences.Length == 1 &&
               parameterIdentifiers[0].Elements().All(element => element.Name.LocalName == "ColumnReference") &&
               string.Equals(parameterReferences[0].Attribute("Column")?.Value, parameter, StringComparison.Ordinal) &&
               !parameterReferences[0].Elements().Any();
    }

    private static bool SqlServerStructuredLogSeekMatches(
        System.Xml.Linq.XElement indexScan,
        string command,
        DiagnosticsNativeRouteSpec specification)
    {
        var seekKeys = indexScan.Descendants().Where(element => element.Name.LocalName == "SeekKeys").ToArray();
        if (seekKeys.Length != 1)
            return false;

        var seekKey = seekKeys[0];
        var prefix = seekKey.Elements().Where(element => element.Name.LocalName == "Prefix").ToArray();
        if (prefix.Length != 1 ||
            seekKey.Elements().Any(element => element.Name.LocalName is not ("Prefix" or "StartRange" or "EndRange")) ||
            !string.Equals(prefix[0].Attribute("ScanType")?.Value, "EQ", StringComparison.Ordinal) ||
            prefix[0].Elements().Any(element => element.Name.LocalName is not ("RangeColumns" or "RangeExpressions")))
            return false;
        if (!SqlServerRangeColumnMatches(prefix[0], "__groundwork_scope", specification.TableName) ||
            !SqlServerRangeExpressionMatches(
                prefix[0],
                SqlServerCommandParameter(command, "__groundwork_scope", "="),
                "nvarchar",
                "216"))
            return false;

        var ranges = seekKey.Elements().Where(element => element.Name.LocalName is "StartRange" or "EndRange").ToArray();
        if (specification.RouteIdentity == "structured-log-recent")
            return ranges.Length == 0;
        if (ranges.Length != 2)
            return false;

        var starts = ranges.Where(element => element.Name.LocalName == "StartRange").ToArray();
        var ends = ranges.Where(element => element.Name.LocalName == "EndRange").ToArray();
        if (starts.Length != 1 || ends.Length != 1)
            return false;
        var start = starts[0];
        var end = ends[0];
        return
               start.Elements().All(element => element.Name.LocalName is "RangeColumns" or "RangeExpressions") &&
               end.Elements().All(element => element.Name.LocalName is "RangeColumns" or "RangeExpressions") &&
               string.Equals(start.Attribute("ScanType")?.Value, "GT", StringComparison.Ordinal) &&
               string.Equals(end.Attribute("ScanType")?.Value, "LE", StringComparison.Ordinal) &&
               SqlServerRangeColumnMatches(start, "sequence", specification.TableName) &&
               SqlServerRangeColumnMatches(end, "sequence", specification.TableName) &&
               SqlServerRangeExpressionMatches(
                   start,
                   SqlServerCommandParameter(command, "sequence", ">"),
                   "bigint") &&
               SqlServerRangeExpressionMatches(
                   end,
                   SqlServerCommandParameter(command, "sequence", "<="),
                   "bigint");
    }

    private static bool SqlServerRangeColumnMatches(
        System.Xml.Linq.XElement range,
        string expectedColumn,
        string expectedTable)
    {
        var columns = range.Elements().Where(element => element.Name.LocalName == "RangeColumns")
            .SelectMany(element => element.Descendants().Where(child => child.Name.LocalName == "ColumnReference"))
            .Select(element => new
            {
                Column = element.Attribute("Column")?.Value,
                Table = element.Attribute("Table")?.Value.Trim('[', ']')
            })
            .ToArray();
        return columns.Length == 1 &&
               string.Equals(columns[0].Column, expectedColumn, StringComparison.Ordinal) &&
               string.Equals(columns[0].Table, expectedTable, StringComparison.Ordinal);
    }

    private static bool SqlServerRangeExpressionMatches(
        System.Xml.Linq.XElement range,
        string? parameter,
        string dataType,
        string? length = null)
    {
        if (parameter is null)
            return false;
        var expressions = range.Elements().Where(element => element.Name.LocalName == "RangeExpressions")
            .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "ScalarOperator"))
            .ToArray();
        return expressions.Length == 1 &&
               SqlServerParameterExpressionMatches(expressions[0], parameter, dataType, length);
    }

    private static string? SqlServerCommandParameter(string command, string column, string @operator)
    {
        var match = Regex.Match(
            NormalizeSqlCommand(command),
            $@"\b{Regex.Escape(column)}\s*{Regex.Escape(@operator)}\s*(?<parameter>@[A-Za-z_][A-Za-z0-9_]*|\?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["parameter"].Value : null;
    }

    private static bool SqlServerParameterExpressionMatches(
        System.Xml.Linq.XElement scalarOperator,
        string parameter,
        string dataType,
        string? length)
    {
        var identifiers = scalarOperator.Elements().Where(element => element.Name.LocalName == "Identifier").ToArray();
        if (identifiers.Length != 1 || scalarOperator.Elements().Any(element => element.Name.LocalName != "Identifier"))
            return false;

        var references = identifiers[0].Elements().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        if (references.Length != 1 ||
            identifiers[0].Elements().Any(element => element.Name.LocalName != "ColumnReference"))
            return false;

        if (string.Equals(references[0].Attribute("Column")?.Value, parameter, StringComparison.Ordinal))
            return !references[0].Elements().Any();
        if (references[0].Elements().Count(element => element.Name.LocalName == "ScalarOperator") != 1 ||
            references[0].Elements().Any(element => element.Name.LocalName != "ScalarOperator"))
            return false;

        var conversionOperator = references[0].Elements().Single();
        var conversions = conversionOperator.Elements().Where(element => element.Name.LocalName == "Convert").ToArray();
        if (conversions.Length != 1 || conversionOperator.Elements().Any(element => element.Name.LocalName != "Convert"))
            return false;
        var conversion = conversions[0];
        if (!string.Equals(conversion.Attribute("DataType")?.Value, dataType, StringComparison.Ordinal) ||
            !string.Equals(conversion.Attribute("Style")?.Value, "0", StringComparison.Ordinal) ||
            !string.Equals(conversion.Attribute("Implicit")?.Value, "1", StringComparison.Ordinal) ||
            (length is not null && !string.Equals(conversion.Attribute("Length")?.Value, length, StringComparison.Ordinal)) ||
            (length is null && conversion.Attribute("Length") is not null))
            return false;

        var nestedOperators = conversion.Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        return nestedOperators.Length == 1 &&
               conversion.Elements().All(element => element.Name.LocalName == "ScalarOperator") &&
               SqlServerParameterExpressionMatches(nestedOperators[0], parameter, dataType, null);
    }

    private static bool SqlServerTopFetchMatches(
        System.Xml.Linq.XDocument document,
        System.Xml.Linq.XElement top,
        string command,
        DiagnosticsNativeRouteSpec specification,
        int expectedFetch)
    {
        var match = Regex.Match(
            NormalizeSqlCommand(command),
            @"\bFETCH\s+(?:FIRST|NEXT)\s+(?<parameter>@[A-Za-z_][A-Za-z0-9_]*|\?)\s+ROWS?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;
        var parameter = match.Groups["parameter"].Value;
        var topExpressions = top.Descendants().Where(element => element.Name.LocalName == "TopExpression")
            .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "ScalarOperator"))
            .ToArray();
        if (topExpressions.Length != 1 ||
            !SqlServerParameterExpressionMatches(topExpressions[0], parameter, "bigint", null))
            return false;

        var parameters = document.Descendants().Where(element => element.Name.LocalName == "ColumnReference" &&
            string.Equals(element.Attribute("Column")?.Value, parameter, StringComparison.Ordinal) &&
            element.Attribute("ParameterRuntimeValue") is not null).ToArray();
        if (parameters.Length != 1 ||
            !string.Equals(parameters[0].Attribute("ParameterDataType")?.Value, "int", StringComparison.Ordinal) ||
            !TryParseSqlServerRuntimeValue(parameters[0].Attribute("ParameterRuntimeValue")?.Value, out var runtimeValue))
            return false;
        return runtimeValue == expectedFetch && expectedFetch == checked(specification.FiniteLimit + 1);
    }

    private static bool TryParseSqlServerRuntimeValue(string? value, out int result)
    {
        result = 0;
        var text = value?.Trim();
        if (text is null || !Regex.IsMatch(text, @"^\(-?\d+\)$", RegexOptions.CultureInvariant))
            return false;
        return int.TryParse(text[1..^1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static bool TrySqlServerRuntimeCount(
        System.Xml.Linq.XElement relop,
        string attribute,
        out long total)
    {
        total = 0;
        var counters = relop.Elements()
            .Where(element => element.Name.LocalName == "RunTimeInformation")
            .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "RunTimeCountersPerThread"))
            .ToArray();
        if (counters.Length == 0)
            return false;
        foreach (var counter in counters)
        {
            var value = counter.Attribute(attribute)?.Value;
            if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 0)
                return false;
            total = checked(total + count);
        }
        return true;
    }
}
