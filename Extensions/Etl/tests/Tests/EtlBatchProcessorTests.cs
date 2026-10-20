using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Tests;

public sealed class EtlBatchProcessorTests
{
    private static readonly EtlBatchProcessor Processor = new(new FileChunkPublisherFactory());
    private const string MainJsonl = "{\"name\":\"A\",\"category\":\"keep\"}\n";
    private const string ErrorJsonl = "{\"recordOrdinal\":3,\"code\":\"etl.lookup-key-not-found\",\"field\":\"id\"}\n";

    [Fact]
    public async Task Processes_rows_once_and_publishes_deterministic_main_and_error_routes()
    {
        using var scenario = await Scenario.CreateAsync(CreatePipeline());
        var result = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);

        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        var acceptance = result.Acceptance!;
        Assert.Equal(1, acceptance.AcceptedRecords);
        Assert.Equal(1, acceptance.RejectedRecords);
        Assert.Equal(1, acceptance.FilteredRecords);
        Assert.Equal(1, acceptance.Batch.StartOrdinal);
        Assert.Equal(4, acceptance.Batch.EndOrdinalExclusive);
        Assert.Equal(scenario.Batch.CanonicalInputBytes, acceptance.Batch.InputBytes);
        Assert.Equal(scenario.Batch.ContentHash, acceptance.Batch.InputHash);
        Assert.Equal(2, acceptance.Effects.Count);

        var main = Assert.Single(acceptance.Effects, effect => effect.Route == EtlOutputRoute.Main);
        var errors = Assert.Single(acceptance.Effects, effect => effect.Route == EtlOutputRoute.Error);
        Assert.Equal(1, main.RecordCount);
        Assert.Equal(1, errors.RecordCount);
        Assert.Equal(Encoding.UTF8.GetByteCount(MainJsonl), main.Chunk.ByteLength);
        Assert.Equal(Encoding.UTF8.GetByteCount(ErrorJsonl), errors.Chunk.ByteLength);
        Assert.Equal(EtlRuntimeIdentity.OutputKey(scenario.Admission, acceptance.Batch.BatchId, EtlOutputRoute.Main), main.Chunk.Key);
        Assert.Equal(EtlRuntimeIdentity.OutputKey(scenario.Admission, acceptance.Batch.BatchId, EtlOutputRoute.Error), errors.Chunk.Key);
        Assert.Equal(Hash(MainJsonl), main.Chunk.ContentHash);
        Assert.Equal(Hash(ErrorJsonl), errors.Chunk.ContentHash);
        Assert.Equal(MainJsonl, await File.ReadAllTextAsync(Path.Combine(scenario.MainRoot, main.Chunk.Key)));
        Assert.Equal(ErrorJsonl, await File.ReadAllTextAsync(Path.Combine(scenario.ErrorRoot, errors.Chunk.Key)));
    }

    [Fact]
    public async Task Accepts_routes_that_exactly_fit_the_combined_output_byte_budget()
    {
        var exactBudget = Encoding.UTF8.GetByteCount(MainJsonl) + Encoding.UTF8.GetByteCount(ErrorJsonl);
        using var scenario = await Scenario.CreateAsync(CreatePipeline(maxOutputBytes: exactBudget));

        var result = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);

        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        Assert.Equal(exactBudget, result.Acceptance!.Effects.Sum(effect => effect.Chunk.ByteLength));
    }

    [Fact]
    public async Task Omits_optional_error_field_and_source_values_from_the_safe_error_route()
    {
        using var scenario = await Scenario.CreateAsync(CreatePipeline(), "id,name\n1,A\n2,B\n9,Z,private-value");

        var result = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);

        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        var error = Assert.Single(result.Acceptance!.Effects, effect => effect.Route == EtlOutputRoute.Error);
        var content = await File.ReadAllTextAsync(Path.Combine(scenario.ErrorRoot, error.Chunk.Key));
        Assert.Equal("{\"recordOrdinal\":3,\"code\":\"etl.field-count-mismatch\"}\n", content);
        Assert.DoesNotContain("private-value", content, StringComparison.Ordinal);
        Assert.DoesNotContain(",\"field\":", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_when_the_combined_routes_exceed_the_batch_output_budget_before_publishing()
    {
        var cap = Math.Max(Encoding.UTF8.GetByteCount(MainJsonl), Encoding.UTF8.GetByteCount(ErrorJsonl));
        using var scenario = await Scenario.CreateAsync(CreatePipeline(maxOutputBytes: cap));

        var result = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);

        Assert.False(result.Succeeded);
        Assert.Null(result.Acceptance);
        Assert.Equal("etl.error-output-too-large", result.Diagnostic!.Code);
        Assert.Empty(Directory.Exists(scenario.MainRoot) ? Directory.EnumerateFiles(scenario.MainRoot) : []);
        Assert.Empty(Directory.Exists(scenario.ErrorRoot) ? Directory.EnumerateFiles(scenario.ErrorRoot) : []);
    }

    [Fact]
    public async Task Replays_identical_effects_at_the_same_keys_and_reports_conflicting_content()
    {
        using var scenario = await Scenario.CreateAsync(CreatePipeline());
        var first = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);
        var replay = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);

        Assert.True(first.Succeeded, first.Diagnostic?.ToString());
        Assert.True(replay.Succeeded, replay.Diagnostic?.ToString());
        Assert.Equal(first.Acceptance!.Effects, replay.Acceptance!.Effects);
        Assert.Single(Directory.EnumerateFiles(scenario.MainRoot, "*.jsonl"));
        Assert.Single(Directory.EnumerateFiles(scenario.ErrorRoot, "*.jsonl"));

        var mainKey = EtlRuntimeIdentity.OutputKey(scenario.Admission, first.Acceptance.Batch.BatchId, EtlOutputRoute.Main);
        await File.WriteAllTextAsync(Path.Combine(scenario.MainRoot, mainKey), "conflicting\n");
        var conflict = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings);

        Assert.False(conflict.Succeeded);
        Assert.Null(conflict.Acceptance);
        Assert.Equal("etl.effect-conflict", conflict.Diagnostic!.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.Fault, conflict.Diagnostic.Kind);
    }

    [Fact]
    public async Task Uses_the_output_revision_retained_by_admission_after_the_current_alias_moves()
    {
        using var scenario = await Scenario.CreateAsync(CreatePipeline());
        var newRoot = Path.Combine(scenario.Root, "main-v2");
        var current = scenario.CreateRetargetedOutputRegistry(newRoot);

        var result = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, current);

        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        var main = Assert.Single(result.Acceptance!.Effects, effect => effect.Route == EtlOutputRoute.Main);
        Assert.True(File.Exists(Path.Combine(scenario.MainRoot, main.Chunk.Key)));
        Assert.False(Directory.Exists(newRoot));
    }

    [Fact]
    public async Task Honors_cancellation_before_processing_without_publishing_chunks()
    {
        using var scenario = await Scenario.CreateAsync(CreatePipeline());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Processor.ProcessAsync(
                scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, scenario.Bindings, cancellation.Token));

        Assert.False(Directory.Exists(scenario.MainRoot));
        Assert.False(Directory.Exists(scenario.ErrorRoot));
    }

    private static EtlPipeline CreatePipeline(long maxOutputBytes = 4096)
    {
        using var filterValue = JsonDocument.Parse("\"keep\"");
        return new EtlPipeline(
            new EtlFileBindings("source", "lookup", "main", "errors"),
            [new("id", EtlColumnType.Int64, Required: true), new("name", EtlColumnType.String, Required: true)],
            [new("id", EtlColumnType.Int64, Required: true), new("category", EtlColumnType.String, Required: true)],
            "id",
            "id",
            [new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")],
            [new("category", EtlComparison.Equal, filterValue.RootElement)],
            new EtlLimits
            {
                PartitionCount = 1,
                MaxRecordBytes = 4096,
                MaxFields = 8,
                MaxBatchRecords = 10,
                MaxBatchBytes = 4096,
                MaxLookupRecords = 100,
                MaxLookupBytes = 4096,
                MaxOutputBatchBytes = maxOutputBytes,
                MaxSnapshotBytes = 64 * 1024
            });
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class Scenario : IDisposable
    {
        private const string SourceText = "id,name\n1,A\n2,B\n9,Z";
        private const string LookupText = "id,category\n1,keep\n2,skip";

        private Scenario(string root, EtlPipeline pipeline, HostFileBindings bindings, EtlRunAdmission admission,
            EtlPreparedDescriptor prepared, EtlInputBatch batch)
        {
            Root = root;
            Pipeline = pipeline;
            Bindings = bindings;
            Admission = admission;
            Prepared = prepared;
            Batch = batch;
            MainRoot = Path.Combine(root, "main");
            ErrorRoot = Path.Combine(root, "errors");
        }

        public string Root { get; }
        public EtlPipeline Pipeline { get; }
        public HostFileBindings Bindings { get; }
        public EtlRunAdmission Admission { get; }
        public EtlPreparedDescriptor Prepared { get; }
        public EtlInputBatch Batch { get; }
        public string MainRoot { get; }
        public string ErrorRoot { get; }
        private EtlScope Scope { get; } = new("tenant-1", "partition-1");

        public static async Task<Scenario> CreateAsync(EtlPipeline pipeline, string sourceText = SourceText)
        {
            var root = Path.Combine(Path.GetTempPath(), $"elsa-etl-processor-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var scope = new EtlScope("tenant-1", "partition-1");
            var sourcePath = Write(root, "source.csv", sourceText);
            var lookupPath = Write(root, "lookup.csv", LookupText);
            var inputsAndOutputs = CreateRegistry(scope, sourcePath, lookupPath,
                Path.Combine(root, "main"), Path.Combine(root, "errors"), includeInputs: true);
            var sourcePin = inputsAndOutputs.Resolve(scope, "source", EtlFileRole.Source);
            var lookupPin = inputsAndOutputs.Resolve(scope, "lookup", EtlFileRole.Lookup);
            var preparer = new EtlFilePreparer(inputsAndOutputs, Path.Combine(root, "snapshots"));
            var snapshots = await preparer.CreateSnapshotsAsync(scope, "run-1", pipeline, sourcePin, lookupPin);
            Assert.True(snapshots.Succeeded, snapshots.Diagnostic?.ToString());
            var preparedResult = await preparer.PrepareAsync(pipeline, snapshots.Snapshots!);
            Assert.True(preparedResult.Succeeded, preparedResult.Diagnostic?.ToString());
            var prepared = preparedResult.Prepared!;

            await using var builder = await EtlInputBatchBuilder.OpenAsync(Path.Combine(root, "snapshots"), pipeline, prepared);
            var build = await builder.BuildNextAsync(0, 1);
            Assert.True(build.Succeeded, build.Diagnostic?.ToString());
            var batch = build.Batch!;
            var pipelineJson = EtlArtifactJson.Write(pipeline);
            var artifact = new EtlArtifact(EtlArtifact.CurrentFormatVersion, "artifact-1",
                EtlArtifactJson.Hash(pipelineJson), "definition-1", 1, pipelineJson);
            var address = new EtlNativeResultAddress(1, "workflow-1", "activity-1", "executable-1", "exec-hash",
                "node-1", "bookmark-1", "registration-1", "resume-1", "stimulus-1", "stimulus-hash",
                "payload-v1", 1, "dedupe-v1", EtlNativeResultAddress.CompletionProviderId);
            var invocation = new EtlInvocation(scope, "run-1", artifact, address);
            var admission = new EtlRunAdmission(invocation, new EtlRunBindings(
                ToRevision(sourcePin), ToRevision(lookupPin),
                ToRevision(inputsAndOutputs.Resolve(scope, "main", EtlFileRole.MainOutput)),
                ToRevision(inputsAndOutputs.Resolve(scope, "errors", EtlFileRole.ErrorOutput))));

            return new Scenario(root, pipeline, inputsAndOutputs, admission, prepared, batch);
        }

        public HostFileBindings CreateRetargetedOutputRegistry(string mainV2Root) =>
            CreateRegistry(Scope, null, null, MainRoot, ErrorRoot, includeInputs: false,
                mainV1Current: false, mainV2Root: mainV2Root);

        private static HostFileBindings CreateRegistry(EtlScope scope, string? sourcePath, string? lookupPath,
            string mainRoot, string errorRoot, bool includeInputs,
            bool mainV1Current = true, string? mainV2Root = null)
        {
            var entries = new List<HostFileBinding>();
            if (includeInputs)
            {
                entries.Add(Input(scope, "source", EtlFileRole.Source, sourcePath!));
                entries.Add(Input(scope, "lookup", EtlFileRole.Lookup, lookupPath!));
            }
            entries.Add(new HostFileBinding(scope, "main", EtlFileRole.MainOutput, "v1", mainRoot,
                mainV1Current, AtomicPublication: true));
            if (mainV2Root is not null)
                entries.Add(new HostFileBinding(scope, "main", EtlFileRole.MainOutput, "v2", mainV2Root,
                    true, AtomicPublication: true));
            entries.Add(new HostFileBinding(scope, "errors", EtlFileRole.ErrorOutput, "v1", errorRoot,
                true, AtomicPublication: true));
            return new HostFileBindings(entries);
        }

        private static HostFileBinding Input(EtlScope scope, string alias, EtlFileRole role, string path) =>
            new(scope, alias, role, "v1", path, true,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), ImmutableInput: true);

        private static string Write(string root, string name, string text)
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        private static EtlBindingRevision ToRevision(PinnedFileBinding pin) =>
            new(pin.Alias, pin.Revision, pin.Handle, pin.ExpectedSha256);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }
}
