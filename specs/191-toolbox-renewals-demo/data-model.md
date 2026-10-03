# Renewal sample
A renewal row has ID, PolicyReference (required), CreatedAt and SchemaVersion. Release 1.1.0 adds ProposedPremium (nullable numeric). Existing rows retain null. Baseline writes stay schema 1.0.0 while older live readers exist; premium access requires schema 2.0.0 finalized. Register-with-premium checks dormancy before any write, then saves once.

Package releases: 1.0.0 and 1.1.0. Activity contracts: explicit 1.0.0 and 1.1.0. Persisted schema family: 1.0.0 and 2.0.0. Workflow design versions are managed separately by Studio.
