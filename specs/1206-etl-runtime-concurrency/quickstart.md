# Validation guide

Prerequisites: .NET 10, Docker with PostgreSQL 16, SQL Server 2022 and the pinned MySQL 8.4 image. Run only owned proof resources, with generated container credentials and ephemeral ports excluding 5095/4179.

1. Run focused real-provider contention and boundary controls while implementing. The evidence will record exact filters; each deliberate mutation must fail for its named reason and pass after restoration.
2. Run complete projects on the stable candidate:
   ```sh
   dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release
   ELSA_ETL_EF_REQUIRE_NATIVE_PROVIDERS=1 dotnet test Extensions/Etl/tests/Persistence/ProviderTests -c Release
   bash tools/architecture/restore-ci-project-graph.sh --locked-mode
   dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release
   dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- solution-filters-check
   dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- test-tiers-check
   ```
3. Build intended Workbench products, then run this spec's owned host controller once per server engine. Assert both ETL store selections, pinned publish/run identities, at least two partitions, exact JSONL records, one native completion and retained readback after host restart. Inspect run/partition state via the real API.
4. Record executing product source and hashes, harness source, full counts/discovery, negative controls and remaining limits under `evidence/`. API and computed-version checks accompany the new exception contract. No run is claimed before its receipt exists.
5. Complete independent draft review, publish gate comment, ready and request review. Required head CI and unmerged base remain merge gates; resulting-main proof is separate. Broader recovery/BPMN/Studio acceptance remains open.
