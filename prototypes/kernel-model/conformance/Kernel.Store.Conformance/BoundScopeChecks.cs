using Kernel.Foundation.Contracts;

namespace Kernel.Store.Conformance;

public static class BoundScopeChecks
{
    public static void Check(List<string> faults)
    {
        foreach (var tenant in new[] { "a", "0-store-9", new string('a', 64) })
            if (new BoundScope(tenant).Tenant != tenant)
                faults.Add("BoundScope changed a valid tenant id");

        foreach (var tenant in new[] { "", "Acme", "acmé", "a_b", new string('a', 65), "a\n" })
        {
            try { _ = new BoundScope(tenant); }
            catch (ArgumentException) { continue; }
            faults.Add("BoundScope accepted a tenant id outside the canonical grammar");
        }
    }
}
