using System.Text;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record ArgumentValue(
    string? Value,
    string? ExpressionType,
    string? Encoding
)
{
    public Encoding? GetEncoding() => !string.IsNullOrWhiteSpace(Encoding)
       ? System.Text.Encoding.GetEncoding(Encoding)
       : null;
}
