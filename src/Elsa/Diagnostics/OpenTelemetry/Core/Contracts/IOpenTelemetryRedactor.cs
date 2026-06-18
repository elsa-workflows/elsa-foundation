using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface IOpenTelemetryRedactor
{
    OpenTelemetryBatch Redact(OpenTelemetryBatch batch);
}
