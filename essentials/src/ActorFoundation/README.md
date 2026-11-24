# Actor Foundation packages

`Elsa.ActorFoundation.Contracts` supplies actor, ingress and read contracts. `Elsa.ActorFoundation.Model.Contracts`
adds the execution-model contract and depends on the foundation contract. Execution models work through these
contracts on any implementation that supports them.

The hosting adapter uses `Elsa.ActorFoundation.Hosting` to bind one configured Embedded tenant for the process before making
ingress or read calls. The constructor of `BoundScope` is internal. The model package has no hosting dependency.

The built-in implementation uses `Elsa.ActorFoundation.BuiltIn.Runtime` for placement and scope activation, and
`Elsa.ActorFoundation.BuiltIn.Hosting` for node scheduling and lifetime. The preview and runtime
conformance programs consume these same assemblies.

`Elsa.ActorFoundation.BuiltIn.Durable` owns the inbox, fenced commits, timers, jobs and journal.
`Elsa.ActorFoundation.BuiltIn.Store.EntityFrameworkCore` exposes the relational implementation for SQLite,
PostgreSQL, SQL Server and MySQL. `Elsa.ActorFoundation.BuiltIn.PostgreSql` composes an ordinary PostgreSQL
node with Generic Host lifetime. Composition is explicit. The host needs an installed store and an actor type.

See the [foundation contract](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/architecture/foundation-contract.md) for the complete behavior and
[the prototype](https://github.com/elsa-workflows/elsa-foundation/blob/main/prototypes/kernel-model/README.md) for the PostgreSQL Core Preview and conformance commands.
