using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Architecture.Tests;

internal static class JsonHashReviewRegister
{
    internal sealed record Site(string Role, string Target, string Operation, int Occurrence,
        IReadOnlyList<string> GenericArguments, string DeclaringTypeContext);
    internal sealed record Owner(string Identity, string Classification, string Reason, IReadOnlyList<string> Sources,
        IReadOnlyList<string> GoldenEvidence, IReadOnlyList<Site> Sites);
    internal sealed record Register(int FormatVersion, string ConsumerSignature, string AuthoritySignature, IReadOnlyList<Owner> Owners);
    private static readonly HashSet<string> Classes = ["json_routed", "mixed_split_routed", "non_json", "physical_file_witness", "physical_row_witness", "shared_authority"];

    internal static IReadOnlyList<Site> Sites(IEnumerable<JsonHashReferenceGraph.Reference> references) => references
        .Where(reference => reference.Role != "ordinary")
        .Select(reference => new Site(reference.Role, reference.Target, reference.Operation, reference.Occurrence,
            reference.GenericArguments, reference.DeclaringTypeContext))
        .OrderBy(site => site.Role, StringComparer.Ordinal).ThenBy(site => site.Target, StringComparer.Ordinal)
        .ThenBy(site => site.Operation, StringComparer.Ordinal).ThenBy(site => site.Occurrence).ToArray();

    internal static IReadOnlyList<string> Validate(JsonHashReferenceGraph graph, Register register, IReadOnlySet<string> authorityOwners)
    {
        var errors = new List<string>();
        if (register.FormatVersion != 1) errors.Add("Unsupported hash review register format.");
        errors.AddRange(graph.Errors);
        var observed = graph.References.Where(reference => reference.Role != "ordinary").GroupBy(reference => reference.Owner)
            .ToDictionary(group => group.Key, group => Sites(group), StringComparer.Ordinal);
        var rows = register.Owners.GroupBy(owner => owner.Identity).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var missing in observed.Keys.Except(rows.Keys, StringComparer.Ordinal)) errors.Add("Unreviewed hash ingress/primitive/gateway: " + missing);
        foreach (var stale in rows.Keys.Except(observed.Keys, StringComparer.Ordinal)) errors.Add("Stale reviewed hash owner: " + stale);
        foreach (var (identity, entries) in rows)
        {
            if (entries.Length != 1) { errors.Add("Duplicate hash review owner: " + identity); continue; }
            var row = entries[0];
            if (!Classes.Contains(row.Classification) || string.IsNullOrWhiteSpace(row.Reason) || row.Sources.Count == 0)
                errors.Add("Hash review needs its classification, reason and authenticated source: " + identity);
            if (authorityOwners.Contains(identity) != (row.Classification == "shared_authority"))
                errors.Add("Only the exact shared authority image may own its primitive allowance: " + identity);
            if (!observed.TryGetValue(identity, out var sites)) continue;
            if (!JsonCanonicalForm.Equals(JsonSerializer.SerializeToElement(sites, JsonBaseline.Options), JsonSerializer.SerializeToElement(row.Sites, JsonBaseline.Options)))
                errors.Add("Changed reviewed hash site/role/generic binding: " + identity);
            if (sites.Any(site => site.Role == "json-hash-input") && row.Classification is not ("json_routed" or "mixed_split_routed" or "shared_authority"))
                errors.Add("JSON gateway must be reviewed as JSON, not as a physical/non-JSON exemption: " + identity);
        }
        return errors;
    }
}
