using Elsa.Persistence.EntityFramework;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>The pure, total schema step for the additive nullable premium column.</summary>
[EfSchemaUpcaster("1.0.0", "2.0.0")]
public sealed class RenewalsOneToTwo : IEfSchemaUpcaster
{
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
