namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Qualifies the actual selected delivery owner and the history provider it records through.</summary>
public interface IRuntimeHistoryDeliverySource
{
    bool CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader);
}

/// <summary>Qualifies the actual selected timer scheduling owner and its durable source store.</summary>
public interface IRuntimeHistoryTimerSource
{
    bool CanRecordHistory(IDurableTimerStore sourceStore);
}
