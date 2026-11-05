# Connection contracts

Host endpoints under `/_elsa/studio/backends` expose personal profile metadata through the existing governed preference service. GET/PUT use verified host identity/tenant and configured HostId; the client cannot select another subject/tenant. Writes require revision preconditions. The namespace rejects unknown fields, invalid URLs, duplicates, oversized values and excessive profile counts. Configuration-owned profiles are never mutated through this API. Demo/anonymous browser-local persistence is a separate mode.

Browser runtime profiles contain public metadata and allowlisted auth behavior only. Server management URL/key are never emitted. A managed authentication-profile reference resolves only against developer-provided public auth configuration. Candidate discovery and login do not reuse the active backend's tokens.

A committed selection navigates to a safe route with a stable backend reference. Resource routes are qualified with that backend. No pending draft, review, token or domain payload is transferred. Candidate sign-in return state is bounded and validates its exact profile fingerprint.

Shared capability discovery follows the existing shell `/capabilities` contract; this work adds no backend feature manifest or configuration contract. Domain-owned contribution requirements use stable IDs, major versions and relative link relations. Capability refresh after feature Apply is scoped to the actual backend session.
