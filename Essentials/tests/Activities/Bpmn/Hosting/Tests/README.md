# Production BPMN hosting qualification

Integration-tier tests launch the built `Elsa.Workbench` as an owned child process. Its configuration is copied to a new temporary content root; Runtime and Design stores are isolated there, and the host chooses its own loopback port. Build-order references locate the actual host assembly even with `--artifacts-path`. Host composition and semantic implementations are not reproduced in the test process.

The initial native witness publishes and executes through normal authenticated APIs, kills only its owned process, restarts against the same stores and verifies the retained execution/pin. This establishes fixture behavior, not direct BPMN hosting or saved-version upgrade acceptance. Later spec1017 cases use the fixture for actual direct/embedded failures and recovery.

```sh
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release
```

The suite runs hosts sequentially. It never discovers or stops an existing listener and never uses a developer's host or data directory.
