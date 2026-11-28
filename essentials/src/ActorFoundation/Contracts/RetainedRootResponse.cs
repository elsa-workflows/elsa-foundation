namespace Kernel.Foundation.Contracts;

/// <summary>Stages the first response of a qualified root in the same transaction as its turn.</summary>
/// <remarks>The opaque key selects a host-owned response channel. The retained response survives
/// root retirement and is available only to the owner that proves the corresponding root birth.</remarks>
public interface IRetainedRootResponseTurn
{
    /// <summary>Retains a bounded response, or a committed end without a response when the payload is null.</summary>
    void RetainFirstResponse(string key, string? payload);
}
