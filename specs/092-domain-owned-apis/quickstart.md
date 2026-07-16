# Quickstart: Validate Domain-Owned Management APIs

This runbook validates the completed feature across `elsa-foundation` and
`elsa-foundation-studio`. Run the sections in order. A successful run proves the delivery sequence in
FR-072, not merely that each repository builds independently.

## 1. Prepare clean sibling worktrees

The Foundation feature worktree is the directory containing this file. Studio is a separate repository
and must be tested from a feature worktree based on its current `origin/main`.

```bash
export FOUNDATION_ROOT=/Users/sipke/.codex/worktrees/552a/elsa-foundation
export STUDIO_REPO=/Users/sipke/Projects/Elsa/elsa-foundation-studio
export STUDIO_ROOT=/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio

git -C "$FOUNDATION_ROOT" status --short --branch
git -C "$STUDIO_REPO" fetch origin
git -C "$STUDIO_REPO" worktree add -b codex/092-domain-owned-apis "$STUDIO_ROOT" origin/main
git -C "$STUDIO_ROOT" status --short --branch
```

If the Studio branch or worktree already exists, reuse it instead of running `worktree add`. Before
validation, both worktrees must contain only the intentional feature changes.

Restore dependencies:

```bash
dotnet restore "$FOUNDATION_ROOT/Elsa.Server.slnx"
cd "$STUDIO_ROOT"
pnpm install --frozen-lockfile
```

## 2. Validate executable retention first

Run the Runtime tests before API tests because publication replacement and executable inspection depend
on correct retention roots.

```bash
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj" \
  --filter 'FullyQualifiedName~WorkflowExecutableReferenceGarbageCollectorTests'
```

The targeted suite must prove all of the following:

- A live source reference protects its executable.
- A retained workflow execution protects its pinned executable even when no live source reference
  remains.
- Running, suspended, completed, canceled, and faulted retained executions all protect the executable.
- An executable becomes eligible for collection only after both its live references and retained
  executions are gone, subject to the configured grace period.
- The collector obtains distinct pinned executable identifiers through the store contract rather than
  loading all workflow executions.

## 3. Validate publication-slot replacement

```bash
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj"
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj" \
  --filter 'FullyQualifiedName~PublicationSlot|FullyQualifiedName~PublishedHttpTrigger'
```

The publication tests must establish this sequence:

1. Publishing a definition whose HTTP trigger is `/foo` to the implicit `default` slot makes `/foo`
   authoritative.
2. Publishing its next version with `/bar` to the same slot atomically makes `/bar` authoritative and
   retires `/foo` for new starts.
3. A failure while validating or activating the `/bar` candidate leaves `/foo` authoritative.
4. Publishing to an explicit second slot keeps both publications only when their trigger cardinality
   permits it; two exclusive HTTP claims conflict.
5. A successful response is not returned while a required trigger projection is still uncommitted.
6. Executions started from the old publication continue with their pinned executable.

The suite should also cover policy precedence (`request > workflow > host`), preflight trigger diffs,
and durable reconciliation when authority and trigger projections cannot share a transaction.

## 4. Validate each domain API slice

```bash
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj"
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj"
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj"
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj"
```

Run the Expressions API and API Capabilities test projects introduced by this feature:

```bash
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Expressions/Api/Tests/Elsa.Expressions.Api.Tests.csproj"
dotnet test "$FOUNDATION_ROOT/tests/Elsa/Api/Capabilities/Tests/Elsa.Api.Capabilities.Tests.csproj"
```

The combined domain tests must prove:

- Workflow Design owns definitions, drafts, persisted versions, metadata, scoped-variable analysis,
  and contextual activity input options.
- Definition creation accepts an optional authored state and has no Sequence/Flowchart-specific
  `rootKind` contract.
- Activity Design returns one normalized authoring catalog and owns availability management.
- Expressions exposes expression and variable-type descriptors independently of `Elsa.Server`.
- Publishing owns source-reference mutation, publication slots and policy, preflight, and test runs.
- Runtime owns executable inspection and execution, workflow instances, and read-only provenance.
- Persisted-version endpoints reject synthetic `draft:` identifiers; instance inspection uses the
  executable pinned by the Runtime record.
- Every endpoint applies its domain authorization policy independently of capability advertisement.

## 5. Validate global capability discovery and shell isolation

The capability integration suite must compose at least two shells with different domain modules and
issue one authenticated `GET /capabilities` request to each shell.

```bash
dotnet test "$FOUNDATION_ROOT/Elsa.Server.slnx" \
  --filter 'FullyQualifiedName~ApiCapabilities|FullyQualifiedName~CapabilityDiscovery'
```

Expected evidence:

- One response contains only stable capability identifiers, contract major versions, and canonical
  relative links for that shell.
- A domain omitted from a shell is absent rather than advertised with a failing link.
- Links do not contain a hard-coded `default` shell name.
- Duplicate or incompatible declarations produce deterministic diagnostics.
- Authentication is required in a secure shell, but the document is otherwise permission-neutral.
- Static declarations come from active features; operational providers contribute only conditional
  state.
- Studio can bootstrap all optional domain experiences from this single request without probing each
  domain endpoint.

For a running integration host, inspect the document directly:

```bash
export BASE_URL=https://localhost:5001
export TOKEN='<management-client access token>'

curl -fsS -H "Authorization: Bearer $TOKEN" "$BASE_URL/capabilities" \
  | jq '.capabilities[] | {id, contractVersion, links}'
```

## 6. Validate a custom host without `Elsa.Server`

The custom-host integration fixture must reference the domain API packages directly and must not
reference the `Elsa.Server` project or copy its endpoint implementation.

```bash
dotnet test "$FOUNDATION_ROOT/Elsa.Server.slnx" \
  --filter 'FullyQualifiedName~CustomHost|FullyQualifiedName~ManagementApiComposition'
```

The fixture must prove that installing selected domain features exposes their canonical links through
`/capabilities`, that omitted domains stay absent, and that the host can perform a representative
definition, catalog, expression, publication, executable, and instance request through the selected
modules. This is the authoritative proof for FR-004; starting the reference application alone is not.

## 7. Validate the coordinated Studio migration

Node 25 enables an experimental process-global Web Storage implementation that conflicts with Vitest's
browser environment unless it is disabled. On Node 25, prefix Studio test commands with
`NODE_OPTIONS=--no-experimental-webstorage`; supported LTS Node versions do not need this compatibility
flag.

Run the route-sensitive packages first:

```bash
cd "$STUDIO_ROOT"
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-weaver-workflows test
```

These tests must cover definition lifecycle, the consolidated activity catalog, expression and variable
descriptors, contextual input options, capability-gated scoped-variable analysis, publication preflight
and slot selection, executable provenance/retirement, Runtime-backed instance inspection, and the Weaver
executable-detail tool.

Then run the complete Studio gates:

```bash
cd "$STUDIO_ROOT"
pnpm typecheck
NODE_OPTIONS=--no-experimental-webstorage pnpm test # Node 25 only; use `pnpm test` on supported LTS Node
pnpm build
pnpm lint
```

Expected outcomes:

- Studio performs one cached capability bootstrap per active backend/shell and follows advertised
  canonical links for optional experiences.
- Editor bootstrap obtains authoring data from the consolidated catalog rather than separate activity
  and descriptor requests.
- Publishing shows the resolved policy and slot, presents preflight additions/removals/conflicts, and
  requires an explicit meaningful slot for side-by-side publication.
- Executable list and inspector use Runtime; unpublish/restore actions mutate Publishing-owned
  publication references rather than deleting Runtime artifacts.
- Instance inspection renders the executable pinned by `artifactId`, including draft test runs, without
  asking Workflow Design to resolve a synthetic version identifier.
- The creation flow builds optional initial authored state from the catalog and sends no `rootKind`.

## 8. Prove the legacy facade and fallbacks are gone

These searches are release gates and must produce no output:

```bash
rg -n 'ElsaWorkflowManagementApi|MapElsaWorkflowManagementApi|/_elsa/workflow-management' \
  "$FOUNDATION_ROOT/src" "$FOUNDATION_ROOT/tests"

rg -n '/_elsa/workflow-management|/_demo/workflows/executables|/descriptors/activities|/descriptors/expression-descriptors|/descriptors/variables' \
  "$STUDIO_ROOT/src" "$STUDIO_ROOT/specs"
```

When a reference host is running, the removed route must return `404` while an advertised canonical link
must reach its domain endpoint (or return the endpoint's domain authorization result for an unauthorized
caller):

```bash
test "$(curl -sk -o /dev/null -w '%{http_code}' "$BASE_URL/_elsa/workflow-management/definitions")" = 404
```

## 9. Run final Foundation and cross-repository gates

```bash
dotnet test "$FOUNDATION_ROOT/Elsa.Server.slnx"
dotnet build "$FOUNDATION_ROOT/Elsa.Server.slnx" --no-restore

cd "$STUDIO_ROOT"
pnpm test
pnpm build
```

The delivery is complete only when both repositories pass from their coordinated feature revisions and
the preceding evidence proves retention, publication replacement, capability discovery, custom-host
composition, canonical Studio usage, and removal of the legacy facade. A green build without those
behavioral proofs is insufficient.
