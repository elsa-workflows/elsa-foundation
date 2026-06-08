# Root/Framework Glossary

These are framework-level terms. Elsa-specific bindings live in [elsa.md](elsa.md).

| Term | Canonical meaning |
|---|---|
| Host | The application process that composes and runs selected modules/features. |
| Module | A package/project boundary that ships code. A module may contain one feature or several closely related features. |
| Feature | A coherent capability with a named activation/configuration surface. Feature identity is used for navigation, composition, and dependency reasoning. |
| Domain | A cohesive area of responsibility with its own language and ownership boundary. A domain should be describable in one verb-led sentence. |
| Application | A concrete host built from the framework by composing selected domains, features, packages, and configuration. |
| Foundation repo | The repository that carries the application baseline: host setup, primitives, main-domain `.Core` libraries, default foundation implementations, and architecture knowledge. |
| `.Core` | The contract layer for a domain or feature: interfaces, models, value objects, events, and exceptions that consumers may reference. |
| Thin implementation | Dependency-light mechanical code such as delegation, wrapping, simple defaults, guards, option binding, or trivial value transformation. |
| Heavy dependency | A dependency that pulls meaningful transitive, native, infrastructure, provider, or engine weight into a package; heavy dependencies are forbidden in `.Core` libraries. |
| Provider | A concrete implementation for a specific backing technology or runtime environment, usually expressed with a provider suffix. |
| Umbrella module | A provider-agnostic module without a provider suffix. It is justified only when real shared provider-neutral code exists. |
| Bundle | A packaging convenience that references other modules without owning new functionality; not a constitutional architecture concept. |
| Multiple features per module | A permitted packaging shape only when the grouped features share the same dependency envelope; a heavy dependency needed by only some features triggers a split. |
| Contribution | Adding an implementation alongside built-ins. The owning domain aggregates all registered contributions through one owner-owned flow. |
| Replacement | Replacing a default implementation of a contract. One implementation wins. |
| Source | A contribution interface that returns values for an owner-owned aggregation flow. |
| Contributor | A contribution interface that receives a context and acts on it for an owner-owned aggregation flow. |
| Startup task | A startup-time task used for composition, registry population, or other deterministic bootstrapping. |
| Event | The in-process message concept represented by `IEvent`. Behavior depends on the publishing strategy. |
| Event strategy | The delivery behavior for an event, such as Sequential, Parallel, or Background. |
| Dependency | A declared or actual requirement from one module/feature/project/package to another. Reason about dependencies at the smallest stable boundary. |
| Compatibility | Whether a set of packages/features can coexist, based on public contracts and external/transitive package requirements. |
| Shell | A composed host/application setup that activates selected features. |
| Work unit | A planned architecture or feature change with intent, acceptance criteria, tasks, and verification. |
| Ratification | Formal acceptance of a constitutional decision by the named architecture decision makers. Draft decisions may guide work, but must remain visibly draft. |
| Capability | Retired vocabulary. Use `feature`. |
| Envelope | Retired vocabulary. Use `module`. |
