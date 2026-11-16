namespace Elsa.Etl.Runtime.Output;

internal sealed class EtlEffectException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string Code { get; } = code;
}
