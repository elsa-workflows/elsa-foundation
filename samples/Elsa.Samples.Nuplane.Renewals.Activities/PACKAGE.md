# Elsa.Samples.Nuplane.Renewals.Activities

Sample Nuplane-loaded `Register renewal` activity. Version `1.0.0` requires `PolicyReference`; version `1.1.0` adds the
optional nullable decimal `ProposedPremium` input and uses the Renewal module's schema-gated premium service.

The local Foundation.Host/Studio demonstration also enables `FoundationDemoDesignerActivities`. It uses the existing CLR metadata scanner on the actual installed Sequence and Flowchart assembly folders, exposing the structural activities to Studio without copying their DLLs beside Foundation.Host or adding host project references.
