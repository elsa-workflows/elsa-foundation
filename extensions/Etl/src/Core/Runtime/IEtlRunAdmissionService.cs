namespace Elsa.Etl.Core.Runtime;

/// <summary>Admits a finite ETL invocation using the host's trusted file bindings.</summary>
public interface IEtlRunAdmissionService
{
    ValueTask<EtlAdmissionResult> AdmitAsync(EtlInvocation invocation, CancellationToken cancellationToken = default);
}
