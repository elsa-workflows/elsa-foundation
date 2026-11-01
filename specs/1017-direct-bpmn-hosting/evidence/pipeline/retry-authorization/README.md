# Actual retry authorization across process recovery

Executing source: `1a10565935b568e42c8431158bfafe77b571c775`. Five standard owned Workbench journeys use direct BPMN and native RunBpmn Root/Sequence/Flowchart/CompensationScope, with probes disabled and the production resumption sweep at0.2 seconds.

Each journey reaches a real HTTP429 wait after two physical output effects, loses its first host through SIGKILL, and faults when its retained lookup snapshot is temporarily unavailable. The exact model invocation remains held at revision2/WaitingIncident. A second SIGKILL and external repair of the unchanged snapshot do not revive the old worker. One authorized retry accepts revision3, admits one different physical native worker/ETL run, completes the same semantic invocation, and preserves the original failed attempt and result receipt. Exact typed native-parent consumption, model settlement and source acknowledgments persist through a fourth standard host. Six physical HTTP records include the previous two again; external effects remain at least once.

The owned SDK creates four ordinary password-backed Identity users per case through the existing user/role services. Stored roles grant read only, retry only, no permissions, or both permissions in a foreign tenant. No execution state is seeded. Actual HTTP login issues the normal session; credentials and cookies are excluded from observations and the private credential request is deleted after setup.

There are355 actual authorization probes and60 sign-ins. Before retry, after accepted retry and after cold restart, anonymous access returns401; the read-only account can inspect but cannot retry; the retry-only account can replay the exact accepted receipt but cannot inspect; accounts without permission or in the foreign tenant receive concealed denials. Denied known/unknown command requests have identical status and no outcome. Middleware may return403; the actual resource denials here return404. Neither is treated as permission to disclose data. Full checked model/wire views remain unchanged by the pre-admission probes; final physical stores retain exactly two workers/runs and the original accepted receipts.

Source audit also exposed an API ordering defect: GET command inspection loaded the retained outcome before resource authorization. This source authorizes the execution, or an outcome-free pending command identity, before lookup. An admission race must then authorize the actual execution before disclosing a retained result. Seven focused regressions fail on the old caller and pass after the fix, including both pending/admitted denials and admission-race reauthorization. The live journeys separately qualify actual production login, role, tenant, replay and recovery behavior; their HTTP responses alone do not establish internal lookup ordering.

The [manifest](manifest.json) binds186 raw assets and45 whole stores. Portable checks compare the actual material/policy/native dependency closure, both physical worker/run roots, exact source and model receipts, immutable ETL preparation, original parent registrations and physical Identity account/role membership against the HTTP sessions. All30 listed children are stopped. Only the API inspection order changes production behavior; runtime/interpreter/persisted writers and the frozen authoring fingerprint are unchanged.

The [diagnostic inventory](diagnostics/manifest.json) retains the first overly strict403 observer and the five pre-order-fix journeys at their original sources. The latter proved role/tenant denials and replay without disclosure, but did not prove authorization preceded GET lookup. They are excluded from this source's qualifying counts.

Run from any checkout:

```sh
python3 e2e-tests/etl/verify_held_service_retry.py --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/retry-authorization/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

Reproduce after a standard committed-source hosting-test build with `-p:ElsaBpmnRecoveryProbe=false`: run `qualify_http_recovery.py --parent BpmnModel --technical-fault --business-error-boundary matching --hold-service-retry --retry-auth-controls`, supplying the built Workbench, DeliveryRecoveryRead and NativeWorkDispatch assemblies and a fresh output path. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for the parent cases. Historical paths/ports are provenance only; never restore them or edit captured execution stores.

[Qualification](qualification.json) retains BPMN418/418, actual direct/native human-host9/9, portable192/192 with16 new controls, maps and123 test-tier registrations; zero skips. The standard host build has0 errors. All104 frozen baseline assets and the original manifest remain unchanged. This increment does not qualify obsolete-result refusal while a replacement is live, timer/competing-input races, remaining provider/retention/rollback or final affected-source upgrade/Studio acceptance. Native composite fault decision#3242 remains undecided. T036/T037, #2898/#2907 and program#2892 remain incomplete; full-PR rounds remain0 of6.
