# Rebuilt integrated correctness

Root's reviewed private harness executed exactly two sequential controls against clean source `b71756fca26ac97c6d464ba491de2d87b14c38be`, Debug Workbench DLL SHA-256 `11b200ec2eea952492549488784b29689904f989e81498d6dbdc08445357cb3b`. Build wrapper exit 0, stable source-before/source-after, 101 existing warnings and zero errors. The HTTP and REST fixtures came from the reviewed clean T02 fixture head `87698620370ca2b7e9e682b1693c4f3296ec3741`. A dedicated PostgreSQL 16.15 container (pinned image `sha256:ef738a34a8651d11b2bace81c55c7e2187f786b484add6c83070884340074368`) and fresh SQLite diagnostic content root were used; existing databases were excluded. Persisted diagnostics stayed enabled and EF logging was Warning. No performance measurement or query-accounting observer was installed.

| Control | Response | Output | Instance status | Incidents | Cadence / cap / inspection |
|---|---|---|---|---:|---|
| http | HTTP 200 | Alice Smith (exact) | Completed | 0 | Coalesced / 50 / boundary-level |
| rest | HTTP 200 | Alice Smith (exact) | Completed | 0 | Coalesced / 50 / boundary-level |

The primary HttpEndpoint contains the deterministic intrinsic SetVariable computation. The independent valid REST companion explicitly projects WorkflowRequest input and exposes the computation with SetOutput. The REST control is not a transport-equivalent latency comparison. Unique exported version/source references were required to equal the published fixture references before execution. Zero incidents was a strict assertion requiring the field to be present, in addition to Completed/output and effective settings. All instance/export/configuration readbacks and exact fixture/helper/script hashes are retained privately.

The host wrapper exited 0. Root independently confirmed the owned PID, container, content root and secret files absent after cleanup; bystander containers and unrelated checkout edits were preserved. Raw diagnostic copies were retained and not opened. Root and independent Sol 5.6 High accepted these bounded sequential controls, exact source/script/build/configuration/reference pins and owned-resource cleanup. Bystander preservation is root-observed; QA independently confirmed owned-resource absence. Final C4 correctness and bounded before/after measurements remain mandatory downstream T17/T18 gates. This record makes no attribution or repair claim for the individual earlier HTTP500/202 responses or the separate failing main SQLite gate.

Reviewed harness source hashes:

- run.ps1: `ad63d3518a13d3824464ab66cf72f5c9429459e77e812ca8467d75d68840ee11`.
- requests.ps1: `f832ff0b8397dbc67112ad19a477462ac1187e4aa8112973ecd6865f5ab20d05`.
- Creator: `af2edc6e5665dbe67c60c8c3bd191720667703c9ebc6be056dab47318e857fe1`; an intentional wrong reviewed-template hash was rejected before any run directory/resource allocation.

Retained actual evidence SHA-256 values:

- metadata.json: `c292fa3588c9f3f90c13c2798327dab03b1e7fef7b3364a74ebb98950a4fe719`.
- http-executable-export.json: `d05d9230fcc276f31a4f71795dea1ccb88b766f50350dd2ddc2373fbbea54b4d`.
- rest-executable-export.json: `8a0a4ae3c5f56cca154b264da707281a57ab734420ff8c83ec2f4cb5771db0ea`.
- http-instance-detail.json: `3f1006f11dc3a628ced10d7d109c4648c9bacd23c355712c212fb940ec44b1c0`.
- rest-instance-detail.json: `c656abc5e50d0319a75231df569dd2142a698e0744bf42dabbc16a76ab57e1b2`.
- fixture.log: `a76d836c3a71c527c29a4ce45b3e58dd453f370be47a5466b016529b80ce022c`.
- wrapper.log: `202137bf9ce7b2f64f22915878e8577002aba63a311d9ddf11e01a435fc13ed9`.
