# Groundwork release adoption

`Test-GroundworkReleaseLifecycle.ps1` is the black-box acceptance path for a Groundwork package update. It
uses the real `Elsa.Workbench` HTTP surface and the default `GroundworkUnifiedPersistenceSqlite` composition:

1. author and save a workflow version;
2. reload that version from the design API;
3. publish the reloaded version;
4. execute it until an Event bookmark is durably suspended; and
5. resume the bookmark and assert the persisted output.

Run it only against a freshly built Workbench and a fresh Groundwork catalog, as described in the parent
[`README.md`](../README.md).
