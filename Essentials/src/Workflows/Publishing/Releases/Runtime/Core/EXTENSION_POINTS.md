# Runtime release bridge contracts

- **Layer:** Publishing release Runtime Core; compiler-free contracts over retained Runtime material.
- **Signatures:** `IReleaseRuntimeArtifactCodec.EncodeWorkflow/EncodeTemplate/EncodeModel/EncodeWorkflowSelection/EncodeActivityVersion`
  accept their typed captured values and optional `ReleaseBundleLimits`, returning `ReleaseEncodedRuntimeMember`;
  matching `Decode*` methods accept `ReadOnlyMemory<byte>` and optional limits, returning the typed captured value.
  `IReleaseRuntimeSiteReader.ReadNode(site, content)` returns `ReleaseRuntimeNodeContext` and `ReadModel(site, content)`
  returns `ReleaseRuntimeModel`. Exact signatures are in [the contracts](ReleaseRuntimeContracts.cs).
- **Default/lifetime:** No Core implementation. Internal scoped defaults in the [Runtime bridge](../Elsa.Workflows.Publishing.Releases.Runtime/EXTENSION_POINTS.md).
- **Override when:** Selecting a bounded lossless owner wire codec or a captured-only site reader with equivalent refusal semantics.
- **Dependent callers:** Runtime artifact verification, model/activity owner site inspectors and Runtime release tests.

`IReleaseRuntimeArtifactCodec` and `IReleaseRuntimeSiteReader` are replacement contracts: select exactly one of each
per shell. Their default scoped implementations live in `Elsa.Workflows.Publishing.Releases.Runtime`; register an
override before its feature or replace the selected descriptor. The feature rejects duplicate registrations.

The typed codec captures original owner IDs, source scope and retained pins in a new versioned release wire envelope.
It does not load CLR types from payloads, compile source, resolve stores or inspect arbitrary model protocols.
Unknown or lossy wire shapes produce a stable `ReleaseRuntimeArtifactException.Code`; no payload or path is included.

The site reader supplies complete captured node/container or neutral model context to exact-key owner inspectors.
It reads only `IReleaseContentReader`; there is no source or target fallback. Site schema 1 uses a node ID envelope,
while the owner's descriptor, structure and model protocol versions remain exact captured values.

Owner strategies use the semantic contracts in `Elsa.Workflows.Publishing.Releases.Core`. This package contributes
no services, events or tasks and grants no origin confidence, catalog import, readiness or serving authority.
