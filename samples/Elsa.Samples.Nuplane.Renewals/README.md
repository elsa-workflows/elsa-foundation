# Elsa.Samples.Nuplane.Renewals

This sample is the Renewal domain for the Nuplane schema-rollout demonstration. It is a package-loaded EF module for
`Elsa.Foundation.Host`. The 3 October 2026 Foundation.Host + Studio browser rehearsal passed; see
[verification](../../specs/191-toolbox-renewals-demo/verification.md) for the observed rollout and limits.

The module is released as `1.0.0` and `1.1.0` from one project using `DemoVersion=1|2` and isolated `bin/v1` / `bin/v2`
outputs. It supports Sqlite and PostgreSql and owns the `SamplesRenewals` schema family.

## Domain and releases

`RenewalRecord` contains `Id`, `PolicyReference`, `CreatedAt`, and the concurrency `SchemaVersion`. The base feature keeps
`POST /demo/renewals` and `GET /demo/renewals` available in both releases. Release `1.0.0` creates the initial renewal table.

Release `1.1.0` adds a nullable decimal `ProposedPremium` column with precision `18,2`, the additive `AddProposedPremium`
migration, and the dormant `RenewalsPremium` feature. It exposes `GET /demo/renewals/with-premium` and
`POST /demo/renewals/with-premium`; both ask `ISchemaDormancyCheck` before reading or writing and return the shared 409
dormancy response until schema version `2.0.0` is finalized, when the release-1.1.0 shell is active. With
`Migrate:Policy=Validate`, a pending migration can instead refuse the new shell during activation; the previous generation
then continues serving the baseline endpoint, so a 409 from these routes must be observed on an active release-1.1.0 host.
A premium-aware registration stores the policy and decimal in one `SaveChangesAsync` call.

`GET /demo/renewals/release` returns the package release, schema family, and readable versions. It is intended as explicit
served-release evidence for the cockpit; the endpoint itself does not claim that a package has been installed or that a
database migration has been applied.

The schema chain is deliberately pure and total from `1.0.0` to `2.0.0`. The nullable scalar needs no JSON content mapping;
the rewriter only restamps a row after the finalization gate admits the new version. Existing rows remain readable with a
null proposed premium, preserving the base feature while the new feature is dormant.

## Package and host composition

The package declares `Samples.Renewals`, `RenewalsEntityFrameworkCore`, `Renewals`, and (in release `1.1.0`) `RenewalsPremium`.
The companion activity package is `Elsa.Samples.Nuplane.Renewals.Activities`; it depends on the matching module release.
The host selects one provider and one connection through its shell configuration. The module does not add a host project
reference or choose a database connection itself.

The `/demo/renewals` observer and registration endpoints are deliberately anonymous for the isolated local cockpit
rehearsal. They are not a production authorization boundary: keep Foundation.Host on loopback and use synthetic policy
references only; do not expose these routes to an external network or send real customer data.

Build or pack both releases only when the enclosing demo explicitly authorizes that operation:

```bash
dotnet pack samples/Elsa.Samples.Nuplane.Renewals/Elsa.Samples.Nuplane.Renewals.csproj -c Release -p:DemoVersion=1
dotnet pack samples/Elsa.Samples.Nuplane.Renewals/Elsa.Samples.Nuplane.Renewals.csproj -c Release -p:DemoVersion=2
```

No live Renewal package, Foundation.Host, database, or Studio run is claimed by this source sample alone.
