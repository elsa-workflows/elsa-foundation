# Data model

- BackendProfile: stable ID, name, canonical full shell BaseUrl, environment, authentication-profile reference, source (configuration/personal). Strict metadata allowlist; no credentials or server management URLs.
- BackendProfiles preference v1: bounded list of unique personal profiles plus last successful profile ID. Key is verified host subject + tenant + HostId + namespace; revision conditional writes prevent lost updates. Tab selection is separate session state.
- BackendSession: immutable profile/endpoint/auth context, subject/tenant and fresh generation key; capability document/result, AbortController, mutation count and disposed state. Contexts never change target.
- ApiCapabilitiesDocument: stable IDs, supported contract major versions, shell-relative relations, optional dormant status/reason. Invalid wire data is an error.
- BackendRequirement: contract ID, supported major and required operation links. Evaluation yields available/unsupported/incompatible/dormant/discovery error with reason; permissions remain separate.
- Feature administration target/review/completion: actual endpoint + backend session key, host kind and pinned context; old target results cannot be committed to a replacement session.
