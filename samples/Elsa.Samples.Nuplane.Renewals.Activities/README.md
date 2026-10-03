# Elsa.Samples.Nuplane.Renewals.Activities

This companion package contributes the `Register renewal` workflow activity for the Renewal module. It is released in lockstep
with `Elsa.Samples.Nuplane.Renewals`, and the package's implementation is selected by `DemoVersion=1|2`.

Release `1.0.0` publishes activity contract `1.0.0` with required `PolicyReference`. Release `1.1.0` publishes contract
`1.1.0`, keeps `PolicyReference` unchanged, and adds optional nullable numeric `ProposedPremium`. The reconciliation descriptor
uses a `String` type for the required policy reference and a `Decimal` type for the optional premium input.

When the premium is supplied, the activity calls the module's `RenewalPremium` service. That service checks
`ISchemaDormancyCheck` before any write and stores the policy and premium in one transaction/save. Without a premium, the
activity uses the base store, so an occurrence pinned to `1.0.0` remains compatible after package `1.1.0` is loaded.

The 3 October 2026 rehearsal passed actual authenticated Foundation.Host + Studio browser execution, explicit selected-node
upgrade, numeric premium execution and untouched 1.0.0 compatibility. See [verification](../../specs/191-toolbox-renewals-demo/verification.md) for evidence and limits.

The `FoundationDemoDesignerActivities` feature reconciles Sequence and Flowchart from their actual Nuplane-installed assembly
folders. It supplies the structural activities needed to create the demo workflow in Studio alongside `Register renewal`.
Compose it with the activity design, runtime and publishing features in `tools/demo/renewals/shells.template.json`.

## Package shape

```text
Activities/       shared RegisterRenewal activity
Reconciliation/   catalog descriptor for the selected activity version
V1/               1.0.0 contract and base store call
V2/               1.1.0 optional decimal input and schema-gated premium call
```

Pack the activity beside the matching module release so Nuplane resolves the same `DemoVersion` dependency:

```bash
dotnet pack samples/Elsa.Samples.Nuplane.Renewals.Activities/Elsa.Samples.Nuplane.Renewals.Activities.csproj -c Release -p:DemoVersion=1
dotnet pack samples/Elsa.Samples.Nuplane.Renewals.Activities/Elsa.Samples.Nuplane.Renewals.Activities.csproj -c Release -p:DemoVersion=2
```
