# Hosting state and ownership

No new persisted domain model is introduced. Existing shell configuration and browser response contracts remain.

| State | Ownership and validation |
|---|---|
| StudioHostingOptions | Registration input: ShellName Default; ShellsFile shells.json or explicit null; IndexFile studio/index.html; QueryTokenPathPrefixes empty. Bind first/callback last. Reject blank/invalid explicit choices. |
| Installed settings | Private immutable copy of selected names/paths/prefixes and auth posture, resolved once against ContentRoot. Later reload cannot mutate installed topology/security. |
| StudioBuilder | Registration-time facade over root services/configuration/environment/framework builder and the root coordinator. Internal constructor; public consumers select modules through CShells callbacks. |
| Mutation coordinator | Root facade over one private process-wide semaphore; AcquireAsync waits cancellably; a granted lease releases exactly once on async disposal. No caller release/dispose access to the semaphore. |
| Selected shell request scope | Registry-owned generation; BeginScope handle owned by the request until all async work completes. Retry only a disposal race during acquisition, once. Framework drain deadlines remain authoritative. |

Shell-file-null means host-owned configuration. It does not authorize writes to an unrelated file. Optional
administration will consume that boundary in #3359; current Web uses its existing configured file composition.
