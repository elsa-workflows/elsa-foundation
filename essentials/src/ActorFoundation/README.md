# Actor Foundation packages

`Elsa.ActorFoundation.Contracts` supplies actor, ingress and read contracts. `Elsa.ActorFoundation.Model.Contracts`
adds the execution-model contract and depends on the foundation contract. Execution models work through these
contracts on any implementation that supports them.

The hosting adapter uses `Elsa.ActorFoundation.Hosting` to bind its configured Embedded tenant before making
ingress or read calls. The constructor of `BoundScope` is internal. The model package has no hosting dependency.

See the [foundation contract](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/architecture/foundation-contract.md) for the complete behavior and
[the prototype](https://github.com/elsa-workflows/elsa-foundation/blob/main/prototypes/kernel-model/README.md) for the PostgreSQL Core Preview and conformance commands.
