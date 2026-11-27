# Elsa.ActorFoundation.BuiltIn.Sqlite

Embedded hosting for an installed SQLite kernel qualification store. The database lives on a local filesystem,
with one process per file. Use the database path directly or through a symbolic link. Keep the database, its WAL
and its `.host-lock` sidecar together, and leave the sidecar in place. Hard links to the database are unsupported.
A clean stop releases the lock. If a stop faults or is killed, the process retains the lock until it exits so a
stuck store call cannot overlap a new host.

This composition uses the owned writer in either fence mode.
