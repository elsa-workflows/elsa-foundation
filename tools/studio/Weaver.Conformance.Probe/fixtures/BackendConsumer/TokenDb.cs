using Microsoft.EntityFrameworkCore;

// Application-owned vendor tables only. Native IAM/Preferences modules retain their own schemas.
public sealed class TokenDb(DbContextOptions<TokenDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
    }
}
