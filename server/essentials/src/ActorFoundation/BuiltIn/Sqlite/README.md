# Elsa.ActorFoundation.BuiltIn.Sqlite

Use `SqliteEmbeddedFoundation.InstallShippedAsync` to create or verify a dedicated SQLite file and bind the composition to it. The same foundation instance supplies the shipped owner, ingress with held replies, and committed reads. For standalone installation, use `SqliteEmbeddedHosting.InstallShippedAsync`. The qualification-format `SqliteEmbeddedHostedService` constructor remains available.

Workbench uses PostgreSQL by default in the Core Preview. To run the shipped preview on SQLite, configure these Workbench settings and use an existing directory with a dedicated database filename.

```text
Elsa__CorePreview__Enabled=true
Elsa__CorePreview__Publications=true
Elsa__CorePreview__Provider=SQLite
Elsa__CorePreview__SqlitePath=/absolute/local/path/kernel.sqlite
```

The database lives on a local filesystem with one process per file. Use the database path directly or through a symbolic link. Keep the database, its WAL and its `.host-lock` sidecar together, and leave the sidecar in place. Hard links to the database are unsupported. A clean stop releases the lock. If a stop faults or the node is killed, the process retains the lock until it exits so a stuck store call cannot overlap a new host.

This composition uses the owned writer in either fence mode.
