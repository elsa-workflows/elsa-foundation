# State and ownership

No new persisted model. Existing Api contracts and browser JSON remain authoritative.

| State | Owner and lifetime |
|---|---|
| Installed auth posture/trusted paths | Hosting registration snapshot, shared with runtime projection; change requires restart. |
| Backend management options | Existing public Api model, root-owned from configuration; server key stays server-side. |
| Session cache | Private root singleton, hashed bearer keys and bounded definitive outcomes; borrowed by shell auth, never static/shared across different root authorities. |
| Permission decision | Existing public requirement/policy contract with private immutable handler; preserves401/403 and local implication. |
| Root session transport/clock | Root-owned session transport and clock borrowed through CShells; general HTTP factories remain shell-owned to preserve module configuration. |
| Backend registry envelope | Existing browser shape, private Hosting implementation; no new public DTO. |
