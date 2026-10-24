# Run and understand the server

Elsa Foundation contains the Elsa 4 server libraries and the reference `Elsa.Workbench` host. Start with [Try Elsa locally](quickstart.md) and [Your first workflow](first-workflow.md) for the recorded Workbench/Studio image pair.

For development and deeper operational guidance:

- [Run the published Workbench and Studio images](https://github.com/elsa-workflows/elsa-foundation/blob/main/docker/compose/README.md#quick-start--published-images-no-clone-or-build) (Docker Compose guide).
- [Build and run the backend from source](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/contributing/backend-source-quickstart.md).
- [Choose a persistence mode](persistence-modes.md): how often a run is committed and what a crash costs.
- [Undo completed steps with a compensation scope](compensation.md): run named steps and undo the completed ones, newest first, when a later step fails.
- Read the [Architecture tour](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/architecture-tour.md), then [How a workflow executes](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/how-a-workflow-executes.md).

The reference host is one composition of Elsa features; a custom host may compose a different set. Module-level notes and API details live beside their projects; the [repository README](https://github.com/elsa-workflows/elsa-foundation/blob/main/README.md#start-here) helps you find your way around the source.

> *Good to know:* guidance linked from `main` describes the development branch. Check the package version you deploy before applying it.

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/essentials/README.md).
