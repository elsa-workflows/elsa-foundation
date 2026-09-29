# Elsa.Samples.Nuplane.Notes

A sample EF module for Elsa.Foundation.Host, loaded from a package feed. It stores notes in one table and is published in two
releases: 1.0.0, and 1.1.0, which adds a nullable `Tags` column, a migration for it, and endpoints that serve tags once
every host sharing the database can read them.

It is a demonstration, not a library. Its source and walkthrough are in the Elsa Foundation repository, under
`samples/Elsa.Samples.Nuplane.Notes`.
