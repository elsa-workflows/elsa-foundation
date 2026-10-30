# Quickstart: import an Elsa 3 folder at engine start

1. In Elsa 3 Studio, export the workflows (each export is one JSON file) and save them in one folder, for example
   `./elsa3-workflows`. Export every reusable workflow ("Usable as activity") the other workflows use.
2. Compose the host's shell with `Elsa3JsonReconciliation` and its dependencies, plus the persistence the import
   needs (`Elsa3ImportActivitiesEntityFrameworkCore`), and configure the folder:

   ```jsonc
   "Elsa3JsonReconciliation": {
     "Options": { "SourceId": "elsa3", "FolderPath": "./elsa3-workflows" }
   }
   ```

3. Start the host and wait for `GET /health/ready`.
4. Check the log for the summary line (`Imported … activities, … workflows; published …; skipped …`) and for a
   warning per skipped workflow with its code and guidance.
5. Open Studio: the reusable workflows are in the activity picker and the workflows are published under their
   Elsa 3 definition ids. Run one, or call its HTTP endpoint.

To add a changed workflow, publish a new version in Elsa 3, export it into the folder and restart: the new version is
imported and published, the old one stays.

Validation scenarios (tests in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`):

| Scenario | Story |
|---|---|
| Empty store, fixture folder → all imported and published; plain and caller run | US1 |
| HTTP Endpoint workflow → trigger registered | US1 |
| Second start → nothing written, nothing published | US2 |
| New version added → only it is imported and published | US2 |
| Commit without publication → next start publishes | US2 |
| Unconvertible reusable + caller → both skipped, rest imported | US3 |
| Strict mode → start fails, nothing committed | US3 |
