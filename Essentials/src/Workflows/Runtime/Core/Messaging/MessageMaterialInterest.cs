using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Messaging;

/// <summary>Finite Message names and start capability of one receiver address in immutable behavior.</summary>
/// <remarks>
/// The address identifies a material node or model element, not a running subscription or an authorization
/// capability. No tenant, logical definition, publication, execution or business key is inferred here.
/// Names are exact ordinal text: no trimming, case folding or Unicode normalization. A declaration contains
/// 1–32 distinct names, each at most 256 UTF-8 bytes. The nonblank material address is opaque UTF-16,
/// preserved without normalization or encoding validation, and bounded to 1024 code units.
/// </remarks>
public sealed class MessageMaterialInterestDeclaration
{
    public MessageMaterialInterestDeclaration(string receiverAddress, IReadOnlyCollection<string> messageNames,
        bool canStartWorkflow = false)
    {
        if (string.IsNullOrWhiteSpace(receiverAddress) || receiverAddress.Length > 1024)
            throw new ArgumentException("A Message receiver address must be nonblank and at most 1024 UTF-16 code units.", nameof(receiverAddress));
        ArgumentNullException.ThrowIfNull(messageNames);
        var names = new List<string>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in messageNames)
        {
            if (names.Count == 32) throw new ArgumentException("A Message declaration exceeds its name bound.", nameof(messageNames));
            MessageInterestText.Require(name, 256, nameof(messageNames));
            if (!distinct.Add(name)) throw new ArgumentException("Message declaration names must be distinct.", nameof(messageNames));
            names.Add(name);
        }
        if (names.Count == 0) throw new ArgumentException("A Message declaration requires a finite nonempty name set.", nameof(messageNames));
        ReceiverAddress = receiverAddress; CanStartWorkflow = canStartWorkflow;
        MessageNames = Array.AsReadOnly(names.Order(StringComparer.Ordinal).ToArray());
    }

    public string ReceiverAddress { get; }
    public IReadOnlyList<string> MessageNames { get; }
    public bool CanStartWorkflow { get; }
    public override string ToString() => "MessageMaterialInterestDeclaration { [redacted] }";
}

/// <summary>Separates an unsupported material profile from a checked material with zero Message receivers.</summary>
/// <remarks>
/// Collections are detached, bounded and ordinally ordered. Each receiver address occurs once; at most 1024
/// receiver/name pairs are permitted in one material. Exceeding a bound refuses the material; never truncate it.
/// This result supplies material facts only. Publication/active-execution owners supply current scope, logical
/// identity and eligibility separately; an empty recognized result is not an acceptance-time cohort snapshot.
/// </remarks>
public sealed class MessageMaterialInterestResult
{
    private MessageMaterialInterestResult(bool recognized, IReadOnlyList<MessageMaterialInterestDeclaration> declarations)
    {
        IsRecognized = recognized; Declarations = declarations;
    }

    public static MessageMaterialInterestResult NotRecognized { get; } = new(false,
        Array.AsReadOnly(Array.Empty<MessageMaterialInterestDeclaration>()));
    public bool IsRecognized { get; }
    public IReadOnlyList<MessageMaterialInterestDeclaration> Declarations { get; }

    public static MessageMaterialInterestResult Recognized(IReadOnlyCollection<MessageMaterialInterestDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var snapshot = new List<MessageMaterialInterestDeclaration>();
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var pairs = 0;
        foreach (var declaration in declarations)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (declaration.MessageNames.Count > 1024 - pairs)
                throw new ArgumentException("Message material exceeds its declaration/name bound.", nameof(declarations));
            if (!addresses.Add(declaration.ReceiverAddress))
                throw new ArgumentException("Message material receiver addresses must be distinct.", nameof(declarations));
            pairs += declaration.MessageNames.Count;
            snapshot.Add(declaration);
        }
        return new(true, Array.AsReadOnly(snapshot.OrderBy(item => item.ReceiverAddress, StringComparer.Ordinal).ToArray()));
    }

    public override string ToString() => "MessageMaterialInterestResult { [redacted] }";
}

/// <summary>A pure material-family strategy for complete Message declarations, with no Design or store reads.</summary>
/// <remarks>
/// Context-selected strategy set: exactly one matching provider must recognize a material. Null ModelProtocol
/// selects native material; a nonnull value selects only that exact model protocol key/version. Several profile
/// strategies may share a selector, but registration order never selects a winner. One native strategy must
/// aggregate every supported native receiver domain; per-activity providers must not compete over whole material.
/// No provider is registered here.
/// Implementations read only checked immutable hash-covered behavior, never authoritative node.Metadata,
/// bookmarks, mutable Design records or evaluated runtime expressions. Include all receive/start declarations,
/// including receive-only nodes that have not run. Validate fixed names or explicit bounded dynamic-name sets;
/// actual registration must independently check the resolved name against its retained material declaration.
/// Unsupported profiles return NotRecognized, not Recognized(empty). Dependency workers are not independently
/// enrolled. Providers do not derive logical identity from a protocol, artifact hash or embedded worker pin.
/// Messaging-disabled hosts do not invoke extraction and retain ordinary publication/execution behavior.
/// </remarks>
public interface IMessageMaterialInterestProvider
{
    /// <summary>Stable nonsecret registration identity, globally distinct, nonblank and at most 256 UTF-8 bytes.</summary>
    string ProviderId { get; }
    RuntimeExecutionProtocol? ModelProtocol { get; }
    MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material);
}

internal static class MessageInterestText
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void Require(string? value, int maximumBytes, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumBytes)
            throw new ArgumentException("A Message declaration value is blank or exceeds its byte bound.", parameter);
        try
        {
            if (Utf8.GetByteCount(value) > maximumBytes)
                throw new ArgumentException("A Message declaration value exceeds its byte bound.", parameter);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("A Message declaration value must contain valid Unicode.", parameter);
        }
    }
}
