# Optional modules

Optional modules add capabilities to an Elsa server, for example the JavaScript and Liquid expression editors, structured logs, OpenTelemetry diagnostics, the dashboard and the Extension Builder. They live in the `Extensions/` folder of the Foundation repository. The JavaScript engine itself lives in `Essentials/src/Expressions`.

A host only gets a module when its composition includes it: a module being in the repository does not mean every host enables it.

- [Extension module overview](https://github.com/elsa-workflows/elsa-foundation/blob/main/Extensions/README.md): the current source layout and module boundary rules.
- [Extension-point index](https://github.com/elsa-workflows/elsa-foundation/blob/main/EXTENSION_POINTS.md): the documented places where you can replace a default or contribute your own implementation.
- [Elsa glossary](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/glossary/elsa.md): framework terminology.

> *Good to know:* these links describe the current development branch. Check your Elsa version and your host's composition before relying on a module or extension point.

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/extensions/README.md).
