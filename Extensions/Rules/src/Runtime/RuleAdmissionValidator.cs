using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

internal sealed class RuleAdmissionValidator(IRuleImplementationIdentitySource identitySource) : IRuleAdmissionValidator
{
    public async ValueTask<RuleAdmissionValidationResult> ValidateAsync(RuleInvocationEnvelope invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = RuleRecordCodec.InvocationBytes(invocation);
        }
        catch (NotSupportedException)
        {
            return new(RuleAdmissionValidationDisposition.Unavailable, new("rules.unsupported-common-schema"));
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return new(RuleAdmissionValidationDisposition.Invalid, new("rules.invalid-common-invocation"));
        }

        var installed = await identitySource.ReadAsync(cancellationToken);
        if (installed is null || RuleRecordCodec.PinHash(installed) != RuleRecordCodec.PinHash(invocation.ExpectedImplementation))
            return new(RuleAdmissionValidationDisposition.Unavailable, new("rules.implementation-unavailable"));
        if (installed.PinSchemaVersion != 1 || installed.LanguageId != RuleLanguage.Id || installed.LanguageVersion != 1
            || installed.ReceiptSchemaVersion != 1 || installed.StateSchemaVersion != 1)
            return new(RuleAdmissionValidationDisposition.Unavailable, new("rules.unsupported-semantic-schema"));

        try
        {
            _ = RuleAdmissionCapacity.Measure(invocation);
            return new(RuleAdmissionValidationDisposition.Ready, null);
        }
        catch (NotSupportedException)
        {
            return new(RuleAdmissionValidationDisposition.Unavailable, new("rules.unsupported-format"));
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return new(RuleAdmissionValidationDisposition.Refused, new("rules.invalid-finite-model"));
        }
    }

    private static bool IsMalformed(Exception exception) => exception is InvalidDataException or ArgumentException
        or JsonException or KeyNotFoundException or InvalidOperationException or FormatException;
}
