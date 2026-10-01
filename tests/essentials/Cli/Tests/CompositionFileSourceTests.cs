using Xunit;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Elsa.Cli.Tests;

public sealed class CompositionFileSourceTests
{
    private const int FileLimit = 1024 * 1024;
    private const string FifoRaceChildMarker = "ELSA_CLI_FIFO_RACE_CHILD";
    private const string FifoRaceDirectory = "ELSA_CLI_FIFO_RACE_DIRECTORY";

    [Fact]
    public void Candidate_reader_counts_bytes_without_trusting_stream_length_and_disposes_the_stream()
    {
        using var bytes = new NonSeekingStream([1, 2, 3, 4]);
        var checks = 0;
        var reader = new CompositionFileReader(_ => checks++, _ => bytes);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, reader.Read("private-file-canary", 4));
        Assert.Equal(2, checks);
        Assert.True(bytes.Disposed);
    }

    [Fact]
    public void Candidate_reader_refuses_the_first_byte_over_the_bound_without_reading_the_rest()
    {
        using var bytes = new NonSeekingStream(new byte[256]);
        var reader = new CompositionFileReader(_ => { }, _ => bytes);

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read("private-file-canary", 4));

        Assert.Equal("candidate-capture-invalid", refusal.Code);
        Assert.Equal(5, bytes.BytesRead);
        Assert.True(bytes.Disposed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(FileLimit + 1)]
    public void Candidate_reader_refuses_unsupported_limits_before_opening(int limit)
    {
        var reader = new CompositionFileReader(_ => throw new InvalidOperationException("must not check"),
            _ => throw new InvalidOperationException("must not open"));

        Assert.Equal("candidate-capture-invalid", Assert.Throws<CliRefusal>(() => reader.Read("unused", limit)).Code);
    }

    [Fact]
    public void Candidate_reader_requires_both_file_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositionFileReader(null!, _ => Stream.Null));
        Assert.Throws<ArgumentNullException>(() => new CompositionFileReader(_ => { }, null!));
    }

    [Fact]
    public void Regular_file_open_lease_opens_once_and_disposes_once()
    {
        var opens = 0;
        var disposals = 0;
        using var lease = new RegularFileOpenLease(() =>
        {
            opens++;
            return new MemoryStream([1, 2, 3]);
        }, () => disposals++);

        using var stream = lease.OpenRead();

        Assert.Equal(1, opens);
        Assert.Throws<InvalidOperationException>(() => lease.OpenRead());
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void Regular_file_open_lease_disposes_when_open_fails()
    {
        var disposals = 0;
        using var lease = new RegularFileOpenLease(() => throw new IOException("private-open-failure"), () => disposals++);

        Assert.Throws<IOException>(() => lease.OpenRead());
        Assert.Equal(1, disposals);
        Assert.Throws<ObjectDisposedException>(() => lease.OpenRead());
    }

    [Fact]
    public void Regular_file_open_lease_requires_both_ownership_callbacks()
    {
        Assert.Throws<ArgumentNullException>(() => new RegularFileOpenLease(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => new RegularFileOpenLease(() => Stream.Null, null!));
    }

    [Fact]
    public void Regular_file_open_lease_refuses_a_null_stream_and_releases_ownership()
    {
        var disposals = 0;
        using var lease = new RegularFileOpenLease(() => null!, () => disposals++);

        Assert.Throws<IOException>(() => lease.OpenRead());
        Assert.Throws<ObjectDisposedException>(() => lease.OpenRead());
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void Regular_file_open_lease_can_be_closed_before_opening()
    {
        var opens = 0;
        var disposals = 0;
        using var lease = new RegularFileOpenLease(() => { opens++; return Stream.Null; }, () => disposals++);

        lease.Dispose();
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => lease.OpenRead());
        Assert.Equal(0, opens);
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void Regular_file_open_lease_does_not_repeat_a_failed_close()
    {
        var disposals = 0;
        using var lease = new RegularFileOpenLease(() => Stream.Null, () => { disposals++; throw new IOException(); });

        Assert.Throws<IOException>(() => lease.Dispose());
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => lease.OpenRead());
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void Candidate_reader_wraps_stream_failures_and_disposes_the_owned_stream()
    {
        using var stream = new UnreadableStream();
        var reader = new CompositionFileReader(_ => { }, _ => stream);

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read("private-path-canary", 4));

        Assert.Equal("composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain("private", refusal.ToString(), StringComparison.Ordinal);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_requires_a_path_collection(bool candidate)
    {
        Assert.Throws<ArgumentNullException>(() => candidate
            ? CompositionInputSnapshot.OpenForCandidate(null!)
            : CompositionInputSnapshot.Open(null!));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("disposed")]
    [InlineData("unreadable")]
    [InlineData("limit")]
    public void Candidate_reader_sanitizes_failed_or_invalid_openers(string failure)
    {
        var reader = new CompositionFileReader(_ => { }, _ =>
        {
            if (failure == "null")
                return null!;
            if (failure == "disposed")
            {
                var stream = new MemoryStream();
                stream.Dispose();
                return stream;
            }
            throw CliRefusal.Usage(failure == "limit" ? "candidate-capture-invalid" : "private-code-canary", "private-message-canary");
        });

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read("private-path-canary", 4));

        Assert.Equal(failure == "limit" ? "candidate-capture-invalid" : "composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain("private", refusal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_reader_accepts_empty_input_at_a_zero_remaining_budget()
    {
        var reader = new CompositionFileReader(_ => { }, _ => new MemoryStream());

        Assert.Empty(reader.Read("unused", 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Candidate_reader_checks_regular_file_before_and_after_read_with_fixed_errors(int failedCheck)
    {
        using var bytes = new NonSeekingStream([1]);
        var checks = 0;
        var opens = 0;
        var reader = new CompositionFileReader(_ =>
        {
            if (++checks == failedCheck)
                throw new IOException("private-file-canary");
        }, _ => { opens++; return bytes; });

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read("private-file-canary", 4));

        Assert.Equal("composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain("private-file-canary", refusal.ToString(), StringComparison.Ordinal);
        Assert.Equal(failedCheck == 1 ? 0 : 1, opens);
        if (opens != 0)
            Assert.True(bytes.Disposed);
    }

    [Fact]
    public void Candidate_reader_rejects_a_fifo_replacing_a_regular_file_after_preflight_without_blocking()
    {
        if (OperatingSystem.IsWindows())
            return;

        if (Environment.GetEnvironmentVariable(FifoRaceChildMarker) == "1")
        {
            RunFifoReplacementProbe(Environment.GetEnvironmentVariable(FifoRaceDirectory)!);
            return;
        }

        using var fixture = new TempDirectory("elsa-candidate-fifo-race-");
        RunFifoReplacementProbeInOwnedChild(fixture.Path);
    }

    private static void RunFifoReplacementProbe(string fixtureDirectory)
    {
        var path = Path.Join(fixtureDirectory, "candidate.json");
        File.WriteAllText(path, "{}");
        var checks = 0;
        var openerReturned = false;
        var reader = new CompositionFileReader(candidatePath =>
        {
            CompositionFileReader.EnsureRegularFile(candidatePath);
            if (Interlocked.Increment(ref checks) == 1)
            {
                File.Delete(candidatePath);
                CreateFifo(candidatePath);
            }
        }, candidatePath =>
        {
            var stream = RegularFileOpener.OpenRead(candidatePath);
            openerReturned = true;
            return stream;
        });

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read(path, FileLimit));

        Assert.Equal("composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain(path, refusal.ToString(), StringComparison.Ordinal);
        Assert.False(openerReturned);
        Assert.Equal(1, checks);
    }

    private static void RunFifoReplacementProbeInOwnedChild(string fixtureDirectory)
    {
        using var resultsDirectory = new TempDirectory("elsa-candidate-fifo-results-");
        var testName = $"{typeof(CompositionFileSourceTests).FullName}.{nameof(Candidate_reader_rejects_a_fifo_replacing_a_regular_file_after_preflight_without_blocking)}";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(CompositionFileSourceTests).Assembly.Location);
        startInfo.ArgumentList.Add($"--TestCaseFilter:FullyQualifiedName={testName}");
        startInfo.ArgumentList.Add($"--ResultsDirectory:{resultsDirectory.Path}");
        startInfo.ArgumentList.Add("--logger:trx;LogFileName=fifo-probe.trx");
        startInfo.Environment[FifoRaceChildMarker] = "1";
        startInfo.Environment[FifoRaceDirectory] = fixtureDirectory;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        var reaped = false;
        var drained = false;
        try
        {
            timedOut = !process.WaitForExit(30_000);
        }
        finally
        {
            if (timedOut || !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It may have exited between the timeout and the kill attempt.
                }
            }

            reaped = process.WaitForExit(5_000);
            if (!reaped)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Continue to the bounded final wait; never leave cleanup unbounded.
                }
                reaped = process.WaitForExit(5_000);
            }

            drained = Task.WaitAll([stdout, stderr], 5_000);
            if (!drained)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Continue to the final bounded waits.
                }
                reaped |= process.WaitForExit(5_000);
                drained = Task.WaitAll([stdout, stderr], 5_000);
            }
        }

        Assert.True(reaped && drained, "The owned FIFO probe process tree could not be reaped and drained within its cleanup deadline.");
        Assert.False(timedOut, "The owned FIFO probe exceeded its 30 second deadline.");
        Assert.Equal(0, process.ExitCode);

        var resultPath = Path.Join(resultsDirectory.Path, "fifo-probe.trx");
        Assert.True(File.Exists(resultPath), "The owned FIFO probe did not produce its result counters.");
        var counters = XDocument.Load(resultPath).Descendants().Single(element => element.Name.LocalName == "Counters");
        Assert.Equal("1", counters.Attribute("total")?.Value);
        Assert.Equal("1", counters.Attribute("executed")?.Value);
        Assert.Equal("1", counters.Attribute("passed")?.Value);
        Assert.Equal("0", counters.Attribute("failed")?.Value);
        Assert.Equal("0", counters.Attribute("notExecuted")?.Value);
    }

    [Fact]
    public void Candidate_reader_rejects_a_symlink_replacing_a_regular_file_after_preflight()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TempDirectory("elsa-candidate-link-race-");
        var path = directory.File("candidate.json");
        var target = directory.File("target.json");
        File.WriteAllText(path, "{}");
        File.WriteAllText(target, "{\"private-canary\":true}");
        var checks = 0;
        var openerReturned = false;
        var reader = new CompositionFileReader(candidatePath =>
        {
            CompositionFileReader.EnsureRegularFile(candidatePath);
            if (Interlocked.Increment(ref checks) == 1)
            {
                File.Delete(candidatePath);
                File.CreateSymbolicLink(candidatePath, target);
            }
        }, candidatePath =>
        {
            var stream = RegularFileOpener.OpenRead(candidatePath);
            openerReturned = true;
            return stream;
        });

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read(path, FileLimit));

        Assert.Equal("composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain(path, refusal.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-canary", refusal.ToString(), StringComparison.Ordinal);
        Assert.False(openerReturned);
        Assert.Equal(1, checks);
    }

    [Fact]
    public void Candidate_reader_refuses_a_symlinked_parent_exchanged_after_preflight()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TempDirectory("elsa-candidate-parent-race-");
        var admitted = directory.File("admitted");
        var moved = directory.File("admitted-before-race");
        var outside = directory.File("outside");
        System.IO.Directory.CreateDirectory(admitted);
        System.IO.Directory.CreateDirectory(outside);
        var path = Path.Join(admitted, "candidate.json");
        File.WriteAllText(path, "{}");
        File.WriteAllText(Path.Join(outside, "candidate.json"), "{\"private-canary\":true}");

        var openerReturned = false;
        var reader = new CompositionFileReader(candidatePath =>
        {
            CompositionFileReader.EnsureRegularFile(candidatePath);
            System.IO.Directory.Move(admitted, moved);
            System.IO.Directory.CreateSymbolicLink(admitted, outside);
        }, candidatePath =>
        {
            var stream = RegularFileOpener.OpenRead(candidatePath);
            openerReturned = true;
            return stream;
        });

        var refusal = Assert.Throws<CliRefusal>(() => reader.Read(path, FileLimit));

        Assert.Equal("composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain("private-canary", refusal.ToString(), StringComparison.Ordinal);
        Assert.False(openerReturned);
    }

    [Fact]
    public void Candidate_reader_keeps_the_admitted_unix_parent_during_preflight_exchange()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TempDirectory("elsa-candidate-unix-parent-lease-");
        var host = directory.File("host");
        var moved = directory.File("host-before-race");
        var outside = directory.File("outside");
        var path = Path.Join(host, "admitted", "candidate.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(Path.Join(outside, "admitted"));
        File.WriteAllText(path, "{\"safe\":true}");
        File.WriteAllText(Path.Join(outside, "admitted", "candidate.json"), "{\"private-canary\":true}");
        var checks = 0;
        var reader = new CompositionFileReader(candidatePath =>
        {
            CompositionFileReader.EnsureRegularFile(candidatePath);
            if (++checks == 1)
            {
                Directory.Move(host, moved);
                Directory.CreateSymbolicLink(host, outside);
            }
        }, RegularFileOpener.OpenRead);

        var bytes = reader.Read(path, FileLimit);

        Assert.Equal("{\"safe\":true}", Encoding.UTF8.GetString(bytes));
        Assert.Equal(2, checks);
        var refusal = Assert.Throws<CliRefusal>(() => new CompositionFileReader().Read(path, FileLimit));
        Assert.Equal("composition-input-unreadable", refusal.Code);
    }

    [Fact]
    public void Candidate_reader_refuses_a_junction_parent_exchanged_after_preflight_on_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var directory = new TempDirectory("elsa-candidate-junction-race-");
        var admitted = directory.File("admitted");
        var moved = directory.File("admitted-before-race");
        var outside = directory.File("outside");
        System.IO.Directory.CreateDirectory(admitted);
        System.IO.Directory.CreateDirectory(outside);
        var path = Path.Join(admitted, "candidate.json");
        File.WriteAllText(path, "{}");
        File.WriteAllText(Path.Join(outside, "candidate.json"), "{\"private-canary\":true}");

        var openerReturned = false;
        var reader = new CompositionFileReader(candidatePath =>
        {
            CompositionFileReader.EnsureRegularFile(candidatePath);
            System.IO.Directory.Move(admitted, moved);
            CreateWindowsJunction(admitted, outside);
        }, candidatePath =>
        {
            var stream = RegularFileOpener.OpenRead(candidatePath);
            openerReturned = true;
            return stream;
        });

        try
        {
            var refusal = Assert.Throws<CliRefusal>(() => reader.Read(path, FileLimit));

            Assert.Equal("composition-input-unreadable", refusal.Code);
            Assert.DoesNotContain("private-canary", refusal.ToString(), StringComparison.Ordinal);
            Assert.False(openerReturned);
        }
        finally
        {
            // Remove the owned junction itself before the enclosing fixture recursively removes its tree.
            if (Directory.Exists(admitted) && (File.GetAttributes(admitted) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(admitted, recursive: false);
        }
    }

    [Fact]
    public void Candidate_reader_retains_all_windows_ancestor_handles_during_preflight()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var directory = new TempDirectory("elsa-candidate-ancestor-lease-");
        var host = directory.File("host");
        var moved = directory.File("host-before-race");
        var admitted = Path.Join(host, "admitted");
        var path = Path.Join(admitted, "candidate.json");
        Directory.CreateDirectory(admitted);
        File.WriteAllText(path, "{\"safe\":true}");
        var exchangeBlocked = false;
        var reader = new CompositionFileReader(candidatePath =>
        {
            CompositionFileReader.EnsureRegularFile(candidatePath);
            try
            {
                Directory.Move(host, moved);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                exchangeBlocked = true;
            }
        }, RegularFileOpener.OpenRead);

        var bytes = reader.Read(path, FileLimit);

        Assert.True(exchangeBlocked, "The retained ancestor handles must block replacement of a checked host directory.");
        Assert.Equal("{\"safe\":true}", Encoding.UTF8.GetString(bytes));
        Assert.False(Directory.Exists(moved));
    }

    [Fact]
    public void Default_candidate_reader_still_captures_regular_files()
    {
        using var directory = new TempDirectory("elsa-candidate-regular-");
        var path = directory.File("candidate.json");
        File.WriteAllText(path, "{\"safe\":true}");

        Assert.Equal("{\"safe\":true}", Encoding.UTF8.GetString(new CompositionFileReader().Read(path, FileLimit)));
    }

    [Fact]
    public void Native_opener_checks_same_handle_type_and_closes_owned_regular_handle()
    {
        using var directory = new TempDirectory("elsa-candidate-handle-");
        var path = directory.File("candidate.json");
        var expected = Encoding.UTF8.GetBytes("{\"safe\":true}");
        File.WriteAllBytes(path, expected);

        using var stream = Assert.IsType<FileStream>(RegularFileOpener.OpenRead(path));
        var handle = stream.SafeFileHandle;
        Assert.False(handle.IsClosed);
        using var captured = new MemoryStream();
        stream.CopyTo(captured);
        Assert.Equal(expected, captured.ToArray());
        stream.Dispose();
        Assert.True(handle.IsClosed);

        Assert.Throws<CliRefusal>(() => RegularFileOpener.OpenRead(directory.Path));
        var specialFile = OperatingSystem.IsWindows() ? Path.Join(Environment.SystemDirectory, "NUL") : "/dev/null";
        Assert.Throws<CliRefusal>(() => RegularFileOpener.OpenRead(specialFile));
    }

    [Fact]
    public void Candidate_source_retains_selected_and_unused_bytes_and_rechecks_with_the_same_reader()
    {
        using var fixture = new LocalFixture();
        var supplied = System.IO.Directory.GetFiles(fixture.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var reader = new CompositionFileReader(_ => { }, path => new MemoryStream(supplied[Path.GetFileName(path)]!));
        var source = CandidateSource(fixture, reader);

        Assert.All(supplied, file => Assert.Equal(file.Value, source.Snapshot.CopyBytes(file.Key!)));
        source.VerifyUnchanged();
        supplied["shells.Staging.json"] = Encoding.UTF8.GetBytes("{}");

        var refusal = Assert.Throws<CliRefusal>(source.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Candidate_source_limits_the_complete_context_including_unused_siblings(bool fileCount)
    {
        using var fixture = new LocalFixture();
        foreach (var file in System.IO.Directory.GetFiles(fixture.Directory))
            File.WriteAllText(file, "{}");
        var totalFiles = fileCount ? 33 : 9;
        for (var i = 6; i < totalFiles; i++)
            File.WriteAllText(Path.Join(fixture.Directory, $"shells.Env{i}.json"), "{}");
        var reader = new CompositionFileReader(_ => { }, _ => new MemoryStream(new byte[fileCount ? 0 : FileLimit]));

        var refusal = Assert.Throws<CliRefusal>(() => CandidateSource(fixture, reader));

        Assert.Equal("candidate-capture-invalid", refusal.Code);
        Assert.DoesNotContain(fixture.Directory, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_capture_accepts_exact_context_bounds_and_keeps_normal_readers_unbounded()
    {
        using var fixture = new LocalFixture();
        for (var i = 6; i < 8; i++)
            File.WriteAllText(Path.Join(fixture.Directory, $"shells.Env{i}.json"), "{}");
        var reader = new CompositionFileReader(_ => { }, _ => new MemoryStream(Encoding.UTF8.GetBytes(new string(' ', FileLimit))));

        var source = CandidateSource(fixture, reader);
        Assert.Equal(8, source.Snapshot.FileNames.Length);
        source.VerifyUnchanged();

        var oversized = Path.Join(fixture.Directory, "appsettings.json");
        File.WriteAllText(oversized, new string(' ', FileLimit + 1));
        Assert.Equal(FileLimit + 1, CompositionFileSource.Open(fixture.Directory, "default", "Production")
            .Snapshot.CopyBytes("appsettings.json").Length);
        Assert.Equal("candidate-capture-invalid", Assert.Throws<CliRefusal>(() => CandidateSource(fixture)).Code);
    }

    [Fact]
    public void Candidate_input_capture_keeps_unused_profiles_frozen_and_rechecks_them()
    {
        using var directory = new TempDirectory("elsa-candidate-input-");
        var selected = directory.File("accepted.json");
        var unused = directory.File("unused-profile.json");
        File.WriteAllText(selected, "{}");
        File.WriteAllText(unused, "{}");
        var content = new Dictionary<string, byte[]> { [selected] = Encoding.UTF8.GetBytes("{}"), [unused] = Encoding.UTF8.GetBytes("{\"unused\":true}") };
        var reader = new CompositionFileReader(_ => { }, path => new MemoryStream(content[path]));
        var input = CompositionInputSnapshot.OpenForCandidate([selected, unused, selected], reader);

        Assert.Equal("{\"unused\":true}", input.ReadText(unused));
        input.VerifyUnchanged();
        content[unused] = Encoding.UTF8.GetBytes("{}");

        Assert.Equal("{\"unused\":true}", input.ReadText(unused));
        Assert.Equal("composition-input-changed", Assert.Throws<CliRefusal>(() => input.VerifyUnchanged()).Code);
    }

    [Fact]
    public void Candidate_inputs_deduplicate_normalized_paths_and_accept_exact_context_bounds()
    {
        using var directory = new TempDirectory("elsa-candidate-input-");
        var paths = Enumerable.Range(0, 8).Select(i => directory.File($"input{i}.json")).ToArray();
        var checks = 0;
        var reader = new CompositionFileReader(_ => checks++, _ => new MemoryStream(new byte[FileLimit]));
        var input = CompositionInputSnapshot.OpenForCandidate([.. paths, paths[0]], reader);

        Assert.Equal(16, checks);
        input.VerifyUnchanged();
        Assert.Equal(32, checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Candidate_input_text_refuses_unknown_path_and_invalid_encoding_without_values(bool invalidEncoding)
    {
        using var directory = new TempDirectory("elsa-candidate-input-");
        var path = directory.File("input.json");
        var reader = new CompositionFileReader(_ => { }, _ => new MemoryStream([0xff]));
        var input = CompositionInputSnapshot.OpenForCandidate([path], reader);

        var refusal = Assert.Throws<CliRefusal>(() => input.ReadText(invalidEncoding ? path : directory.File("private-canary.json")));

        Assert.Equal(invalidEncoding ? "composition-input-invalid" : "composition-input-unreadable", refusal.Code);
        Assert.DoesNotContain("private-canary", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_input_growth_or_loss_after_capture_is_a_drift_refusal()
    {
        using var directory = new TempDirectory("elsa-candidate-input-");
        var path = directory.File("input.json");
        File.WriteAllText(path, "{}");
        var input = CompositionInputSnapshot.OpenForCandidate([path]);
        File.WriteAllText(path, new string(' ', FileLimit + 1));

        Assert.Equal("composition-input-changed", Assert.Throws<CliRefusal>(input.VerifyUnchanged).Code);
        File.Delete(path);
        Assert.Equal("composition-input-changed", Assert.Throws<CliRefusal>(input.VerifyUnchanged).Code);
    }

    [Fact]
    public void Existing_intent_reader_keeps_its_unbounded_behavior()
    {
        using var directory = new TempDirectory("elsa-candidate-input-");
        var path = directory.File("input.json");
        File.WriteAllText(path, new string(' ', FileLimit + 1));
        var input = CompositionInputSnapshot.Open([path]);

        Assert.Equal(FileLimit + 1, input.ReadText(path).Length);
        input.VerifyUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Candidate_source_detects_added_or_removed_unused_siblings(bool remove)
    {
        using var fixture = new LocalFixture();
        var source = CandidateSource(fixture);
        if (remove)
            File.Delete(Path.Join(fixture.Directory, "shells.Staging.json"));
        else
            File.WriteAllText(Path.Join(fixture.Directory, "shells.New.json"), "{}");

        Assert.Equal("bridge-source-changed", Assert.Throws<CliRefusal>(source.VerifyUnchanged).Code);
    }

    [Theory]
    [InlineData(33, 0)]
    [InlineData(9, FileLimit)]
    [InlineData(1, FileLimit + 1)]
    public void Candidate_inputs_enforce_unique_file_count_actual_file_bytes_and_context_bytes(int count, int size)
    {
        using var directory = new TempDirectory("elsa-candidate-input-");
        var paths = Enumerable.Range(0, count).Select(i => directory.File($"input{i}.json")).ToArray();
        var reader = new CompositionFileReader(_ => { }, _ => new MemoryStream(new byte[size]));

        Assert.Equal("candidate-capture-invalid", Assert.Throws<CliRefusal>(() => CompositionInputSnapshot.OpenForCandidate(paths, reader)).Code);
    }

    [Theory]
    [InlineData("shells.Unused.json", false)]
    [InlineData("unused-profile.json", true)]
    public void Candidate_capture_refuses_unused_fifo_before_content_read(string name, bool intent)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new LocalFixture();
        var fifo = Path.Join(fixture.Directory, name);
        CreateFifo(fifo);

        var refusal = Assert.Throws<CliRefusal>(() =>
        {
            if (intent)
                _ = CompositionInputSnapshot.OpenForCandidate([fifo]);
            else
                _ = CandidateSource(fixture);
        });

        Assert.Equal(intent ? "composition-input-unreadable" : "bridge-source-unreadable", refusal.Code);
    }

    private static CompositionFileSource CandidateSource(LocalFixture fixture, CompositionFileReader? reader = null) =>
        CompositionFileSource.OpenForCandidate(fixture.Directory, "default", "Production", reader);

    private static void CreateFifo(string path)
    {
        var executable = File.Exists("/usr/bin/mkfifo") ? "/usr/bin/mkfifo" : "/bin/mkfifo";
        using var process = Process.Start(new ProcessStartInfo(executable) { ArgumentList = { path } });
        Assert.NotNull(process);
        if (!process.WaitForExit(2_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It may have exited after the bounded wait.
            }
            var reaped = process.WaitForExit(2_000);
            Assert.True(reaped, "The owned mkfifo process did not terminate after the fixture deadline.");
            Assert.Fail("mkfifo did not finish within the fixture deadline.");
        }
        Assert.Equal(0, process.ExitCode);
    }

    private static void CreateWindowsJunction(string path, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/c", "mklink", "/J", path, target }
        });
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(5_000), "mklink did not finish within the fixture deadline.");
        Task.WaitAll(output, error);
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class NonSeekingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public int BytesRead { get; private set; }
        public override long Length => throw new NotSupportedException("Length must not govern byte bounds.");
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class UnreadableStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("private-stream-canary");
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void Open_freezes_supported_siblings_and_rechecks_unselected_files()
    {
        using var fixture = new LocalFixture();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");

        Assert.Equal(6, source.Snapshot.FileNames.Length);
        Assert.Contains("shells.Staging.json", source.Snapshot.FileNames);
        source.VerifyUnchanged();

        File.AppendAllText(Path.Join(fixture.Directory, "shells.Staging.json"), " ");
        var refusal = Assert.Throws<CliRefusal>(source.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
    }

    [Fact]
    public void Open_rechecks_the_selected_overlay_after_preview()
    {
        using var fixture = new LocalFixture();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");

        File.AppendAllText(Path.Join(fixture.Directory, "shells.Production.json"), " ");

        var refusal = Assert.Throws<CliRefusal>(source.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
    }

    [Fact]
    public void Open_requires_the_named_shell_overlay_without_echoing_the_host_path()
    {
        using var fixture = new LocalFixture();

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFileSource.Open(fixture.Directory, "default", "Absent"));

        Assert.Equal("bridge-source-missing", refusal.Code);
        Assert.DoesNotContain(fixture.Directory, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_rejects_a_symbolic_link_in_the_supported_bundle()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new LocalFixture();
        var link = Path.Join(fixture.Directory, "shells.Staging.json");
        File.Delete(link);
        File.CreateSymbolicLink(link, Path.Join(fixture.Directory, "shells.Production.json"));

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFileSource.Open(fixture.Directory, "default", "Production"));

        Assert.Equal("bridge-source-unreadable", refusal.Code);
    }

    [Fact]
    public void Disposable_workbench_files_import_without_portable_setting_values_or_host_activation()
    {
        using var fixture = new LocalFixture();
        fixture.UseWorkbenchFiles();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");
        var snapshot = source.Snapshot;
        var draft = new SelectionCatalog("1", "workbench-compatibility", "1", "elsa-foundation", new string('0', 64), [], []);
        var catalog = draft with { Digest = SelectionDigest.ComputeCatalogDigest(draft) };

        var imported = CompositionImporter.Import(
            snapshot.ReadText("shells.json"),
            snapshot.ReadText(snapshot.Selection.ShellOverlayFileName),
            snapshot.ReadText("appsettings.json"),
            snapshot.Selection.AppsettingsOverlayFileName is { } appOverlay ? snapshot.ReadText(appOverlay) : null,
            "default", "Production", catalog);

        Assert.NotEmpty(imported.Authored.Add);
        Assert.Null(imported.Authored.Settings);
        Assert.Contains("shells.baseline.json", snapshot.FileNames);
        Assert.All(imported.Preview.Settings, setting => Assert.Null(setting.PortableValue));
        source.VerifyUnchanged();
    }

    [Fact]
    public void Disposable_workbench_files_generate_an_unchanged_candidate_without_host_activation()
    {
        using var fixture = new LocalFixture();
        fixture.UseWorkbenchFiles();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");
        var snapshot = source.Snapshot;
        var draft = new SelectionCatalog("1", "workbench-compatibility", "1", "elsa-foundation", new string('0', 64), [], []);
        var catalog = draft with { Digest = SelectionDigest.ComputeCatalogDigest(draft) };
        var imported = CompositionImporter.Import(
            snapshot.ReadText("shells.json"),
            snapshot.ReadText(snapshot.Selection.ShellOverlayFileName),
            snapshot.ReadText("appsettings.json"),
            snapshot.Selection.AppsettingsOverlayFileName is { } appOverlay ? snapshot.ReadText(appOverlay) : null,
            "default", "Production", catalog);

        var candidate = CompositionCandidateBuilder.Build(snapshot, catalog, imported.Authored, review: null);

        Assert.Equal(snapshot.FileNames.Order(StringComparer.Ordinal), candidate.Files.Keys.Order(StringComparer.Ordinal));
        Assert.All(candidate.Files, file => Assert.Equal(snapshot.CopyBytes(file.Key), file.Value));
        Assert.All(candidate.Changes, change => Assert.False(change.Changed));
        source.VerifyUnchanged();
    }

    private sealed class LocalFixture : IDisposable
    {
        public string Directory { get; } = Path.Join(Path.GetTempPath(), "elsa-composition-source-" + Guid.NewGuid().ToString("N"));

        public LocalFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            var source = Path.Join(RepoRoot, "tests", "essentials", "Cli", "Tests", "Fixtures", "CompositionBridge");
            foreach (var file in System.IO.Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Join(Directory, Path.GetFileName(file)));
        }

        public void UseWorkbenchFiles()
        {
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
                File.Delete(file);
            var workbench = Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench");
            foreach (var file in System.IO.Directory.GetFiles(workbench, "*.json"))
                if (Path.GetFileName(file).StartsWith("shells", StringComparison.Ordinal) ||
                    Path.GetFileName(file).StartsWith("appsettings", StringComparison.Ordinal))
                    File.Copy(file, Path.Join(Directory, Path.GetFileName(file)));
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static string RepoRoot
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    directory = directory.Parent;
                return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
            }
        }
    }
}
