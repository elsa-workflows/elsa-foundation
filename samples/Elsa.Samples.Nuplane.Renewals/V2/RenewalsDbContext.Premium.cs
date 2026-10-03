using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Samples.Nuplane.Renewals;

public abstract partial class RenewalsDbContext
{
    partial void ConfigureReleaseModel(EntityTypeBuilder<RenewalRecord> entity) =>
        entity.Property(renewal => renewal.ProposedPremium)
            .HasColumnType("numeric(18,2)")
            .HasPrecision(18, 2);
}
