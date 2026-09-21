# Architecture baselines

The frozen ASP.NET Core Identity EF oracle, its ratchet, and the repository-wide `ef-core-surface.json`
inventory were all retired (issue #1482). EF Core absence is now asserted by
`EfCoreDependencyGuardTests`, which walks the declared csproj graph and needs no baseline or restore.
