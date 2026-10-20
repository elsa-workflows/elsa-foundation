using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;

namespace Elsa.Etl.Runtime.Bindings;

/// <summary>Resolves exact host revisions without using authored paths or retargeting retained runs.</summary>
internal sealed class HostFileBindings
{
    private readonly IReadOnlyList<(HostFileBinding Binding, PinnedFileBinding Pin)> _bindings;

    public HostFileBindings(IReadOnlyList<HostFileBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var snapshots = new List<(HostFileBinding, PinnedFileBinding)>();
        var revisions = new HashSet<(EtlScope, string, EtlFileRole, string)>();
        var current = new HashSet<(EtlScope, string, EtlFileRole)>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.Scope is null || !ValidName(binding.Alias) || !ValidName(binding.Revision) ||
                !Enum.IsDefined(binding.Role) || string.IsNullOrWhiteSpace(binding.Path) || !Path.IsPathFullyQualified(binding.Path))
                throw new EtlBindingException("etl.binding-invalid", "An ETL host file binding is invalid.");

            var input = binding.Role is EtlFileRole.Source or EtlFileRole.Lookup;
            if (input && (!binding.ImmutableInput || !ValidHash(binding.ExpectedSha256)))
                throw new EtlBindingException("etl.binding-input-not-immutable", "An ETL input requires a declared immutable revision and SHA-256.");
            if (!input && !binding.AtomicPublication)
                throw new EtlBindingException("etl.binding-output-not-atomic", "An ETL output requires create-only atomic publication.");
            if (!revisions.Add((binding.Scope, binding.Alias, binding.Role, binding.Revision)) ||
                binding.IsCurrent && !current.Add((binding.Scope, binding.Alias, binding.Role)))
                throw new EtlBindingException("etl.binding-ambiguous", "An ETL host binding has ambiguous revisions.");

            var snapshot = binding with { Path = Path.GetFullPath(binding.Path), ExpectedSha256 = input ? binding.ExpectedSha256!.ToLowerInvariant() : null };
            var identity = JsonSerializer.SerializeToUtf8Bytes(new
            {
                FormatVersion = 1, snapshot.Scope, snapshot.Alias, snapshot.Role, snapshot.Revision,
                snapshot.Path, snapshot.ExpectedSha256, snapshot.ImmutableInput, snapshot.AtomicPublication
            });
            var handle = Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
            snapshots.Add((snapshot, new PinnedFileBinding(snapshot.Scope, snapshot.Alias, snapshot.Role,
                snapshot.Revision, handle, snapshot.ExpectedSha256)));
        }
        _bindings = snapshots.AsReadOnly();
    }

    public IReadOnlyList<FileBindingInfo> List(EtlScope scope) => _bindings
        .Where(x => x.Binding.IsCurrent && x.Binding.Scope == scope)
        .Select(x => new FileBindingInfo(x.Binding.Alias, x.Binding.Role))
        .OrderBy(x => x.Alias, StringComparer.Ordinal).ThenBy(x => x.Role).ToArray();

    public PinnedFileBinding Resolve(EtlScope scope, string alias, EtlFileRole role)
    {
        var match = _bindings.SingleOrDefault(x => x.Binding.IsCurrent && x.Binding.Scope == scope &&
            x.Binding.Alias == alias && x.Binding.Role == role);
        return match.Pin ?? throw Unavailable();
    }

    public Stream OpenInput(PinnedFileBinding pin)
    {
        var binding = Find(pin);
        if (binding.Role is not (EtlFileRole.Source or EtlFileRole.Lookup))
            throw Unavailable();
        try
        {
            return new FileStream(binding.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EtlBindingException("etl.binding-read-failed", "The pinned ETL input is unavailable.", exception);
        }
    }

    public string GetOutputRoot(PinnedFileBinding pin)
    {
        var binding = Find(pin);
        if (binding.Role is not (EtlFileRole.MainOutput or EtlFileRole.ErrorOutput))
            throw Unavailable();
        return binding.Path;
    }

    private HostFileBinding Find(PinnedFileBinding pin) => _bindings.FirstOrDefault(x => x.Pin == pin).Binding ?? throw Unavailable();

    private static bool ValidName(string? value) => value is { Length: > 0 and <= 128 } && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static EtlBindingException Unavailable() => new("etl.binding-unavailable", "The scoped ETL binding revision is unavailable for this role.");
}
