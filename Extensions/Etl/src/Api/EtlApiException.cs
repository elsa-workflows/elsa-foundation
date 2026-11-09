namespace Elsa.Etl.Api;

internal sealed class EtlApiException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
