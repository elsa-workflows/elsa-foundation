using Elsa.DesignPersistence.Benchmarks.Forms;
using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Stores;
using Elsa.DesignPersistence.Benchmarks.Workload;

namespace Elsa.DesignPersistence.Benchmarks;

/// <summary>Resolves a (adapter, form) selection into a concrete benchmark target.</summary>
public static class TargetFactory
{
    /// <summary>The <c>store</c> form token selects the real-store catalog path; the physical-form
    /// tokens select the modeled SQLite form path.</summary>
    public static IBenchmarkTarget Create(string adapter, string form, DesignWorkload workload)
    {
        adapter = adapter.Trim().ToLowerInvariant();
        form = form.Trim().ToLowerInvariant();

        return (adapter, form) switch
        {
            ("ef", "store" or "normalized") => new EfStoreTarget(workload),
            ("groundwork", "store") => new GroundworkStoreTarget(workload),
            ("groundwork", "shared" or "dedicated" or "entity") => new SqliteFormTarget(PhysicalForms.Parse(form), workload),
            ("ef", _) => throw new ArgumentException("The EF oracle only supports the 'store' form."),
            _ => throw new ArgumentException($"Unknown adapter/form combination '{adapter}/{form}'.")
        };
    }

    /// <summary>The evidence key a (adapter, form) selection produces, without constructing it.</summary>
    public static string TargetKey(string adapter, string form) => (adapter.Trim().ToLowerInvariant(), form.Trim().ToLowerInvariant()) switch
    {
        ("ef", _) => "ef.normalized",
        ("groundwork", "store") => "groundwork.store",
        ("groundwork", var f) => $"groundwork.{f}",
        _ => throw new ArgumentException($"Unknown adapter/form combination '{adapter}/{form}'.")
    };

    /// <summary>The (adapter, form) targets measured for a scale in the orchestrated matrix.</summary>
    public static IReadOnlyList<(string Adapter, string Form)> MatrixTargets(WorkloadScale scale)
    {
        var storePath = new[] { ("ef", "store"), ("groundwork", "store") };
        var formPath = new[] { ("groundwork", "shared"), ("groundwork", "dedicated"), ("groundwork", "entity") };

        // 1M repeats only the scale-bearing form comparison; the EF-ratio store path is decided at 100K.
        if (scale.Name == WorkloadScale.OneMillion.Name)
            return formPath;
        return storePath.Concat(formPath).ToList();
    }
}
