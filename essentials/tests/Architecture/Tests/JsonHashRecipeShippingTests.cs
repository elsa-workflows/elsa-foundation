using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashRecipeShippingTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_shipping_hash_ingress_primitive_and_gateway_has_a_current_reviewed_recipe()
    {
        var configuration = GetType().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var setup = Stopwatch.StartNew();
        using var inventory = ShippingHashAssemblies.Load(RepoPaths.RepoRoot, configuration);
        Assert.True(inventory.Errors.Count == 0, string.Join('\n', inventory.Errors));
        var graph = JsonHashReferenceGraph.Scan(inventory.Assemblies);
        Assert.True(graph.Errors.Count == 0, string.Join('\n', graph.Errors));
        var maps = new Dictionary<Guid, JsonHashSourceDocuments>();
        var external = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var project in inventory.Projects)
        {
            var map = JsonHashSourceDocuments.Read(project.AssemblyPath, RepoPaths.RepoRoot,
                new Dictionary<string, string> { ["/_/"] = RepoPaths.RepoRoot }, project.CompileSources,
                project.IntermediateRoot is null ? null : [project.IntermediateRoot]);
            Assert.True(map.Errors.Count == 0 && map.ImageMvid == project.ImageMvid, project.Path + "\n" + string.Join('\n', map.Errors));
            maps.Add(map.ImageMvid, map);
            foreach (var pair in project.PackageSources ?? new Dictionary<string, string>())
            {
                if (external.TryGetValue(pair.Key, out var existing)) Assert.Equal(existing, pair.Value);
                external[pair.Key] = pair.Value;
            }
        }
        setup.Stop();
        var evidence = Path.Join(Path.GetTempPath(), "elsa-s9-recipe-measurements-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Join(evidence, "setup.json"), JsonSerializer.Serialize(new
        { Projects = inventory.Projects.Count, Seconds = setup.Elapsed.TotalSeconds, Definitions = graph.Definitions.Count, References = graph.References.Count }));
        var modules = inventory.Assemblies.SelectMany(assembly => assembly.Modules).ToDictionary(module => module.ModuleVersionId);
        var documentedKickoffs = maps.ToDictionary(pair => pair.Key, pair => pair.Value.Methods.Values
            .Where(method => method.KickoffToken is not null && method.Documents.Count != 0).Select(method => method.KickoffToken!.Value).ToHashSet());
        using (var unresolved = new StreamWriter(Path.Join(evidence, "all-undocumented-bodies.jsonl")))
        {
            foreach (var definition in graph.Definitions.Values.Where(definition => definition.Kind == "method"))
            {
                var map = maps[definition.ModuleMvid];
                if (!map.Methods.TryGetValue(definition.MetadataToken, out var method) || method.Documents.Count != 0 ||
                    documentedKickoffs[definition.ModuleMvid].Contains(definition.MetadataToken)) continue;
                var callable = modules[definition.ModuleMvid].ResolveMethod(definition.MetadataToken)!;
                if (callable.GetMethodBody() is not { } body || JsonHashRecipeReceipt.CanWitnessWithoutDocument(callable)) continue;
                unresolved.WriteLine(JsonSerializer.Serialize(new { Owner = definition.Identity, Kind = callable.MemberType.ToString(),
                    Instructions = IlInstructionReader.Read(body.GetILAsByteArray()!).Select(instruction => instruction.OpCode.Name).Take(24).ToArray() }));
            }
        }
        var authority = inventory.Assemblies.Where(JsonHashReferenceGraph.IsHashAuthorityAssembly)
            .Select(assembly => assembly.ManifestModule.ModuleVersionId).ToHashSet();
        var groups = graph.References.Where(reference => reference.Role != "ordinary").GroupBy(reference => reference.Owner)
            .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var authorityOwners = groups.Where(group => authority.Contains(graph.Definitions[group.Key].ModuleMvid) &&
                (graph.Definitions[group.Key].RuntimeType == typeof(Elsa.Serialization.Core.JsonHashInput).FullName ||
                 graph.Definitions[group.Key].RuntimeType?.StartsWith(typeof(Elsa.Serialization.Core.JsonHashInput).FullName + "+", StringComparison.Ordinal) == true))
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var registerPath = Path.Join(RepoPaths.RepoRoot, "essentials/tests/Architecture/Tests/Baselines/json-hash-review.json");
        var reviewed = File.Exists(registerPath) ? JsonSerializer.Deserialize<JsonHashReviewRegister.Register>(File.ReadAllText(registerPath), Elsa.Serialization.Core.JsonBaseline.Options)!
            : new JsonHashReviewRegister.Register(1, "", "", []);
        var reviewsByOwner = reviewed.Owners.GroupBy(row => row.Identity).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var watch = Stopwatch.StartNew();
        var consumers = JsonHashRecipeReceipt.CreateMany(groups.Select(group => group.Key).Except(authorityOwners, StringComparer.Ordinal), graph, inventory.Assemblies, maps, external);
        var authorityReceipt = JsonHashRecipeReceipt.CreateMany(authorityOwners, graph, inventory.Assemblies, maps, external);
        watch.Stop();
        var observed = new JsonHashReviewRegister.Register(1, consumers.Signature, authorityReceipt.Signature, groups.Select(group =>
        {
            var definition = graph.Definitions[group.Key];
            var map = maps[definition.ModuleMvid];
            var callable = modules[definition.ModuleMvid].ResolveMethod(definition.MetadataToken)!;
            var sources = map.Methods[definition.MetadataToken].Documents.Concat(callable.DeclaringType is { } declaring
                    ? map.TypeDocuments.GetValueOrDefault(declaring.MetadataToken) ?? [] : [])
                .Distinct(StringComparer.Ordinal).Select(path => map.Documents[path].Origin == "external-compile" ? external[path] : path)
                .Order(StringComparer.Ordinal).ToArray();
            reviewsByOwner.TryGetValue(group.Key, out var row);
            return new JsonHashReviewRegister.Owner(group.Key, row?.Classification ?? "UNREVIEWED", row?.Reason ?? "Review required.",
                sources, row?.GoldenEvidence ?? [], JsonHashReviewRegister.Sites(group));
        }).ToArray());
        var goldens = JsonHashReviewRegister.ReadGoldenEvidence(RepoPaths.RepoRoot, observed.Owners);
        observed = observed with { GoldenSignature = goldens.Signature };
        File.WriteAllText(Path.Join(evidence, "observed-review.json"), JsonSerializer.Serialize(observed, Elsa.Serialization.Core.JsonBaseline.Options));
        File.WriteAllText(Path.Join(evidence, "union-measurements.json"), JsonSerializer.Serialize(new
        {
            Seconds = watch.Elapsed.TotalSeconds, PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            Consumers = new { consumers.Signature, consumers.Definitions, consumers.Contracts, consumers.Sources },
            Authority = new { authorityReceipt.Signature, authorityReceipt.Definitions, authorityReceipt.Contracts, authorityReceipt.Sources }
        }));
        output.WriteLine("Exhaustive shipping hash review evidence: " + evidence);
        var errors = JsonHashReviewRegister.Validate(graph, reviewed, authorityOwners).ToList();
        errors.AddRange(JsonHashReviewRegister.ValidateGoldenEvidence(reviewed, goldens));
        if (reviewed.ConsumerSignature != consumers.Signature) errors.Add("Changed consumer producer/DTO/converter/source closure. Review its classification and actual-owner goldens before updating the receipt.");
        if (reviewed.AuthoritySignature != authorityReceipt.Signature) errors.Add("Changed shared JsonHashInput authority. Apply the first-stable hash freeze and explicit migration rule before updating the receipt.");
        foreach (var row in reviewed.Owners)
            if (observed.Owners.FirstOrDefault(owner => owner.Identity == row.Identity) is { } current &&
                !current.Sources.SequenceEqual(row.Sources, StringComparer.Ordinal)) errors.Add("Changed authenticated owner sources: " + row.Identity);
        File.WriteAllLines(Path.Join(evidence, "errors.txt"), errors);
        Assert.True(errors.Count == 0, string.Join('\n', errors.Take(20)) + "\nEvidence: " + evidence);

    }
}
