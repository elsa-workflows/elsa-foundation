# Elsa.Samples.Nuplane.Renewals

Sample Nuplane-loaded EF module for policy renewals. Release `1.0.0` stores policy references. Release `1.1.0` adds a
nullable decimal `ProposedPremium` column and a schema-gated premium feature. Existing renewal rows remain readable while
the additive migration is pending.
