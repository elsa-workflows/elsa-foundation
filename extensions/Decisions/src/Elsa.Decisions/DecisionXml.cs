using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Elsa.Decisions.Core;

namespace Elsa.Decisions;

internal sealed record DecisionInput(string Id, string ExpressionId, string Name, string Type);
internal sealed record DecisionOutput(string Id, string Name, string Type);
internal sealed record DecisionRow(string Id, string[] InputIds, UnaryTest[] Tests, string[] OutputIds, DecisionValue[] Values);
internal sealed record DecisionTable(string Id, string TableId, string Policy, DecisionInput[] Inputs, DecisionOutput[] Outputs, DecisionRow[] Rows);

internal sealed class DecisionXml
{
    internal const string Namespace = "https://www.omg.org/spec/DMN/20191111/MODEL/";
    internal const string Profile = "elsa-dmn-table-v1";
    internal const string Semantics = "elsa-dmn-table-v1.0";
    internal const int MaximumXmlBytes = 1_048_576;
    internal const int MaximumOperations = 1_000_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> Reserved = new("and between context else every external false for function if in instance list not null of or return satisfies some then true".Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> DiagramNamespaces = new(StringComparer.Ordinal)
    {
        "https://www.omg.org/spec/DMN/20191111/DMNDI/", "http://www.omg.org/spec/DMN/20180521/DC/", "http://www.omg.org/spec/DMN/20180521/DI/",
        "https://www.omg.org/spec/DMN/20180521/DC/", "https://www.omg.org/spec/DMN/20180521/DI/"
    };
    private readonly List<DecisionDiagnostic> diagnostics = [];
    private readonly CancellationToken token;
    private string? decisionId;

    internal DecisionXml(CancellationToken token) => this.token = token;
    internal IReadOnlyList<DecisionDiagnostic> Diagnostics => diagnostics;
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static byte[] Bytes(string value) => Utf8.GetBytes(value);

    internal DecisionTable? Parse(string xml)
    {
        token.ThrowIfCancellationRequested();
        if (xml is null || xml.Length > MaximumXmlBytes || Utf8.GetByteCount(xml) > MaximumXmlBytes) { Error("DocumentTooLarge"); return null; }
        // StringReader does not consume a byte-order signature as a byte stream reader does.
        // Only the parser view drops one leading signature; compilation hashes the exact original bytes.
        var parserXml = xml.Length > 0 && xml[0] == '\uFEFF' ? xml[1..] : xml;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes,
            IgnoreComments = false, IgnoreProcessingInstructions = false, CloseInput = true
        };
        using (var guard = XmlReader.Create(new StringReader(parserXml), settings))
        {
            var elements = 0;
            while (guard.Read())
            {
                token.ThrowIfCancellationRequested();
                if (guard.Depth > 64 || guard.NodeType == XmlNodeType.Element && ++elements > 100_000)
                { Error("DocumentLimitExceeded"); return null; }
            }
        }
        using var reader = XmlReader.Create(new StringReader(parserXml), settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        token.ThrowIfCancellationRequested();
        var root = document.Root;
        if (root?.Name != XName.Get("definitions", Namespace)) { Error("UnsupportedDocument"); return null; }
        Id(root, true);
        ValidateTree(root);
        var decisions = root.Elements(N("decision")).ToArray();
        if (decisions.Length != 1) { Error("DecisionCount"); return null; }
        var decision = decisions[0];
        decisionId = Id(decision, true);
        var tables = decision.Elements(N("decisionTable")).ToArray();
        if (tables.Length != 1) { Error("TableRequired"); return null; }
        var table = tables[0];
        var tableId = Id(table, true);
        var policy = (string?)table.Attribute("hitPolicy") ?? "UNIQUE";
        if (policy is not ("UNIQUE" or "ANY" or "FIRST" or "RULE ORDER" or "COLLECT")) Error("UnsupportedHitPolicy", table);
        if (table.Attribute("aggregation") is not null) Error("UnsupportedAggregation", table);
        if (table.Attribute("preferredOrientation") is { } orientation && orientation.Value != "Rule-as-Row") Error("UnsupportedOrientation", table);

        var inputElements = table.Elements(N("input")).ToArray();
        var outputElements = table.Elements(N("output")).ToArray();
        var ruleElements = table.Elements(N("rule")).ToArray();
        if (inputElements.Length is < 1 or > 32 || outputElements.Length is < 1 or > 16 || ruleElements.Length is < 1 or > 1000)
        { Error("TableDimensions", table); return null; }
        var inputs = inputElements.Select(input =>
        {
            var id = Id(input, true);
            var expressions = input.Elements(N("inputExpression")).ToArray();
            if (expressions.Length != 1) { Error("InputExpressionRequired", input); return new DecisionInput(id, "", "", ""); }
            var expression = expressions[0];
            var name = CellText(expression).Trim();
            if (!ValidSymbol(name)) Error("InvalidInputSymbol", input);
            return new DecisionInput(id, Id(expression, true), name, Type(expression));
        }).ToArray();
        var outputs = outputElements.Select(output =>
        {
            var name = (string?)output.Attribute("name") ?? "";
            if ((outputElements.Length > 1 || output.Attribute("name") is not null) &&
                (string.IsNullOrWhiteSpace(name) || name.Length > 256 || !DecisionScalars.ValidString(name))) Error("InvalidOutputName", output);
            return new DecisionOutput(Id(output, true), name, Type(output));
        }).ToArray();
        if (inputs.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() != inputs.Length) Error("DuplicateInputName", table);
        if (outputs.Select(o => o.Name).Distinct(StringComparer.Ordinal).Count() != outputs.Length) Error("DuplicateOutputName", table);
        ValidateInputData(root, decision, inputs);
        if (decision.Elements(N("variable")).Skip(1).Any()) Error("DuplicateDecisionVariable", decision);
        var variable = decision.Element(N("variable"));
        if (variable?.Attribute("typeRef") is not null && (outputs.Length != 1 || Type(variable) != outputs[0].Type)) Error("DecisionVariableType", variable);

        var rows = new List<DecisionRow>();
        foreach (var rule in ruleElements)
        {
            token.ThrowIfCancellationRequested();
            var ruleId = Id(rule, true);
            var entries = rule.Elements(N("inputEntry")).ToArray();
            var values = rule.Elements(N("outputEntry")).ToArray();
            if (entries.Length != inputs.Length || values.Length != outputs.Length) { Error("RuleWidth", rule); continue; }
            var tests = new UnaryTest[entries.Length];
            var results = new DecisionValue[values.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                try { tests[i] = DecisionCellParser.ParseTest(CellText(entries[i]), token); }
                catch (DecisionLanguageException exception) { Error(exception.Code, entries[i], ruleId, inputs[i].Id); }
            }
            for (var i = 0; i < values.Length; i++)
            {
                try
                {
                    results[i] = DecisionCellParser.ParseOutput(CellText(values[i]), token);
                    if (results[i].Kind != "null" && results[i].Kind != outputs[i].Type) Error("OutputTypeMismatch", values[i], ruleId, outputs[i].Id);
                }
                catch (DecisionLanguageException exception) { Error(exception.Code, values[i], ruleId, outputs[i].Id); }
            }
            rows.Add(new DecisionRow(ruleId, entries.Select(e => Id(e, true)).ToArray(), tests, values.Select(e => Id(e, true)).ToArray(), results));
        }
        if (diagnostics.Count > 0) return null;
        // Upper bound includes all cells/alternatives/outputs, ANY comparisons, validation and rule selection.
        var cost = inputs.Length + rows.Sum(row => 1 + row.Tests.Sum(test => 1 + test.Alternatives.Length) + outputs.Length * 3 + 2);
        if (cost > MaximumOperations) { Error("PublicationBudgetExceeded", table); return null; }
        return new DecisionTable(decisionId, tableId, policy, inputs, outputs, rows.ToArray());
    }

    private void ValidateTree(XElement root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.DescendantsAndSelf())
        {
            token.ThrowIfCancellationRequested();
            var id = (string?)element.Attribute("id");
            if (id is not null && (id.Length is 0 or > 256 || !IsXmlId(id) || !ids.Add(id))) Error("InvalidOrDuplicateId", element);
            if (element.AncestorsAndSelf().Any(e => DiagramNamespaces.Contains(e.Name.NamespaceName))) continue;
            if (element.Name.NamespaceName != Namespace) { Error("UnsupportedExtension", element); continue; }
            var allowedChildren = element.Name.LocalName switch
            {
                "definitions" => "description decision inputData DMNDI extensionElements",
                "decision" => "description variable informationRequirement decisionTable extensionElements",
                "inputData" => "description variable extensionElements",
                "informationRequirement" => "description requiredInput extensionElements",
                "decisionTable" => "description input output rule annotation extensionElements",
                "input" => "description inputExpression extensionElements",
                "output" or "annotation" or "variable" => "description extensionElements",
                "rule" => "description inputEntry outputEntry annotationEntry extensionElements",
                "inputExpression" or "inputEntry" or "outputEntry" or "annotationEntry" => "description text extensionElements",
                "description" or "text" or "requiredInput" or "extensionElements" => "",
                _ => null
            };
            if (allowedChildren is null) { Error("UnsupportedElement", element); continue; }
            var allowedAttributes = element.Name.LocalName switch
            {
                "definitions" => "id name namespace expressionLanguage typeLanguage exporter exporterVersion label",
                "decisionTable" => "id hitPolicy preferredOrientation label aggregation",
                "variable" or "output" => "id name typeRef label",
                "inputExpression" => "id typeRef expressionLanguage label",
                "inputEntry" or "outputEntry" => "id expressionLanguage label",
                "requiredInput" => "href",
                "description" or "text" or "extensionElements" => "",
                _ => "id name label"
            };
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                if (attribute.Name.NamespaceName == "http://camunda.org/schema/modeler/1.0" && attribute.Name.LocalName is "executionPlatform" or "executionPlatformVersion") continue;
                if (attribute.Name.NamespaceName.Length != 0 || !allowedAttributes.Split(' ').Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                    Error("UnsupportedAttribute", element);
                if (attribute.Name.LocalName is "expressionLanguage" or "typeLanguage" && attribute.Value != "https://www.omg.org/spec/DMN/20191111/FEEL/")
                    Error("UnsupportedLanguage", element);
                if (attribute.Value.Length > 4096) Error("MetadataTooLarge", element);
            }
            foreach (var child in element.Elements())
            {
                if (element == root && child.Name == XName.Get("DMNDI", "https://www.omg.org/spec/DMN/20191111/DMNDI/")) continue;
                if (child.Name.NamespaceName != Namespace || !allowedChildren.Split(' ').Contains(child.Name.LocalName, StringComparer.Ordinal)) Error("UnsupportedElement", child);
            }
            if (element.Name.LocalName is not ("text" or "description") && element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) Error("UnexpectedText", element);
            if (element.Name.LocalName is "text" or "description" && element.HasElements) Error("UnsupportedElement", element);
        }
    }

    private void ValidateInputData(XElement root, XElement decision, DecisionInput[] inputs)
    {
        var data = root.Elements(N("inputData")).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inputData in data)
        {
            Id(inputData, true);
            var variables = inputData.Elements(N("variable")).ToArray();
            if (variables.Length != 1) { Error("InputDataVariable", inputData); continue; }
            var variable = variables[0];
            Id(variable, true);
            var name = (string?)variable.Attribute("name") ?? (string?)inputData.Attribute("name");
            var input = inputs.FirstOrDefault(i => i.Name == name);
            if (name is null || !names.Add(name)) Error("DuplicateInputDataName", inputData);
            if (input is null || variable.Attribute("typeRef") is not null && Type(variable) != input.Type) Error("InputDataMismatch", inputData);
        }
        var dataIds = data.Select(d => (string?)d.Attribute("id")).ToHashSet(StringComparer.Ordinal);
        foreach (var requirement in decision.Elements(N("informationRequirement")))
        {
            var refs = requirement.Elements(N("requiredInput")).ToArray();
            if (refs.Length != 1 || !((string?)refs[0].Attribute("href") is { } href && href.StartsWith('#') && dataIds.Contains(href[1..])))
                Error("UnsupportedDependency", requirement);
        }
    }

    private string Type(XElement element)
    {
        var type = (string?)element.Attribute("typeRef") ?? "";
        if (type is not ("number" or "string" or "boolean")) Error("UnsupportedType", element);
        return type;
    }
    private string CellText(XElement element)
    {
        var texts = element.Elements(N("text")).ToArray();
        if (texts.Length != 1 || texts[0].Value.Length is 0 or > 4096) { Error("CellTextRequired", element); return ""; }
        return texts[0].Value;
    }
    private string Id(XElement element, bool required)
    {
        var value = (string?)element.Attribute("id") ?? "";
        if (required && value.Length == 0) Error("MissingId", element);
        return value;
    }
    private static bool IsXmlId(string value) { try { XmlConvert.VerifyNCName(value); return true; } catch (XmlException) { return false; } }
    private static bool ValidSymbol(string value) => value.Length is > 0 and <= 256 && !Reserved.Contains(value) && Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static XName N(string value) => XName.Get(value, Namespace);
    private void Error(string code, XElement? element = null, string? rule = null, string? clause = null)
    {
        if (diagnostics.Count >= 256) return;
        diagnostics.Add(new(code, "Decision content does not satisfy the supported profile.", decisionId, rule ?? element?.AncestorsAndSelf(N("rule")).FirstOrDefault()?.Attribute("id")?.Value, clause ?? (string?)element?.Attribute("id")));
    }

    internal static byte[] Canonical(DecisionTable table, CancellationToken token)
    {
        var ns = XNamespace.Get(Namespace);
        XElement Text(string value) { token.ThrowIfCancellationRequested(); return new(ns + "text", value); }
        var root = new XElement(ns + "definitions", new XAttribute("id", "definitions"), new XAttribute("namespace", "urn:elsa:decision"),
            new XElement(ns + "decision", new XAttribute("id", table.Id),
                new XElement(ns + "decisionTable", new XAttribute("id", table.TableId), new XAttribute("hitPolicy", table.Policy),
                    table.Inputs.Select(input => new XElement(ns + "input", new XAttribute("id", input.Id),
                        new XElement(ns + "inputExpression", new XAttribute("id", input.ExpressionId), new XAttribute("typeRef", input.Type), Text(input.Name)))),
                    table.Outputs.Select(output => new XElement(ns + "output", new XAttribute("id", output.Id), table.Outputs.Length > 1 ? new XAttribute("name", output.Name) : null, new XAttribute("typeRef", output.Type))),
                    table.Rows.Select(row => new XElement(ns + "rule", new XAttribute("id", row.Id),
                        row.Tests.Select((test, i) => new XElement(ns + "inputEntry", new XAttribute("id", row.InputIds[i]), Text(test.Format()))),
                        row.Values.Select((value, i) => new XElement(ns + "outputEntry", new XAttribute("id", row.OutputIds[i]), Text(DecisionScalars.Format(value)))))))));
        // The synthetic root ID must never collide with a preserved semantic ID.
        var used = root.Descendants().Attributes("id").Select(a => a.Value).ToHashSet(StringComparer.Ordinal);
        var definitionId = "definitions";
        while (used.Contains(definitionId)) definitionId += "_";
        root.SetAttributeValue("id", definitionId);
        return Bytes(root.ToString(SaveOptions.DisableFormatting));
    }
}
