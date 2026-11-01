# Optional Backends module — #3596

Owner authorization: extract the delivered Backends management from Hosting into an optional module; frontend and backend extensibility seams are explicitly permitted. Program: none/free-flow. This work follows #3531/#3536; it does not undo their isolation and feature-management contracts.

## Required behavior

- `Elsa.Studio.Backends` / `BackendsStudio` owns profile configuration, storage namespace, HTTP endpoints, CRUD/inspection UI and switching. Disabling the feature removes those registrations/endpoints and client contributions. Omitting its package leaves a usable Studio host without a Preferences dependency.
- Core owns one configured backend, stable Studio identity authority, authenticated immutable endpoint lifetimes and capability discovery. It always supplies a real backend session, including when management is absent.
- Core never imports Backends implementation or reads its configuration/profile storage. Optional modules use public generic extension points; independently named extensions must work. Ambiguous replacement providers and duplicate runtime namespaces fail explicitly.
- Preserve separate profile views/tabs, source ownership, independent authentication, production confirmation, failed/cancelled candidate retention, draft/write guards, resource-qualified links and exact-target Features review/Apply.
- Unknown explicit backend references fail closed before resource requests. Safe Home reset discards the foreign resource; absence must never reinterpret B's IDs against A.
- Feature reload retires the old client lifetime and reconstructs the provider through a safe document boundary. Ordinary shell reload preserves default in-memory metadata for the same host; two hosts remain isolated. Restart/package-generation durability uses the existing explicit durable provider.

## Acceptance

1. Enabled: existing source-host CRUD, auth/isolation, capability and feature-setting journeys pass.
2. Disabled: bad optional configuration and stored foreign selection are ignored; there are no profile endpoints/namespace/store or optional JS/CSS imports. Configured-backend workflows and Features still work.
3. Absent: a genuine Hosting-only PackageReference consumer has no Backends/Preferences closure and exercises the default session and capabilities.
4. Disable/re-enable: safe default landing, fresh provider/session identities and retained metadata; public endpoint routing works at root and with a Studio prefix.
5. A differently named connection extension and shell action work through the public SDK; duplicate providers fail explicitly.
6. Existing scope/revision, malformed payload, bounded request, SQLite restart, credential and late-result controls remain meaningful.
