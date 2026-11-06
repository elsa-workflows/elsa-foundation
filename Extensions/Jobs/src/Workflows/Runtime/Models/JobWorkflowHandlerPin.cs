using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Services;

namespace Elsa.Jobs.Workflows.Runtime.Models;

/// <summary>Publication-owned logical handler/schema selections and accepted policy, independent of a worker deployment.</summary>
public sealed record JobWorkflowHandlerPin(int FormatVersion, JobHandlerIdentity Handler,
    JobSchema InputSchema, JobSchema ResultSchema, string Queue, JobExecutionPolicy Policy,
    JobAssociationMode AssociationMode)
{
    /// <summary>Writes the bounded versioned node descriptor consumed by runtime-only coordinators.</summary>
    /// <exception cref="JobException">The descriptor is invalid or cannot be represented by the supported contract.</exception>
    public string Serialize()
    {
        JobsNativeBoundary.ValidatePin(this);
        var json = JobsNativeBoundary.Write(this).GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 32_768) throw JobsNativeBoundary.Invalid();
        return json;
    }

    /// <summary>Reads an immutable published descriptor without consulting a mutable definition or installed handler.</summary>
    /// <exception cref="JobException">The descriptor is missing, invalid or unsupported.</exception>
    public static JobWorkflowHandlerPin Parse(string json)
    {
        var value = JobsNativeBoundary.ReadString<JobWorkflowHandlerPin>(json, 32_768);
        JobsNativeBoundary.ValidatePin(value);
        return value;
    }
}
