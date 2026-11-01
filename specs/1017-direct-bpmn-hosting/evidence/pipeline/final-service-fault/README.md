# Actual explicit final-fault retry refusal

Executing source: `6f532c6edfe4931ce0d574e0c98c5d93cb012d81`. The direct model pins explicit FinalFault/one total attempt. The real HTTP429/SIGKILL/retained-lookup-unavailable journey faults the original ETL/native worker and model. The coded business boundary does not catch the uncoded technical fault.

The incident remains Final with its exact original invocation/failed attempt and pre-failure interpreter snapshot. An authorized retry using those actual identities returns 409 and records Rejected/execution-terminal at unchanged model revision 2. No new worker, ETL run or physical HTTP effects appear. The original two outputs and terminal truth survive another standard host; all four listed children stopped. Snapshot bytes are restored unchanged only after the hosts stop.

The [manifest](manifest.json) binds 22 raw assets and six whole stores. `verify_http_recovery.py` checks the actual policy/material, original failed worker/ETL/model result, exact rejected retry receipt, unchanged pins, lookup bytes and continued terminality. This is separate from [held retry](../held-service-retry/README.md), budget exhaustion and final upgraded-host/Studio qualification.

```sh
python3 e2e-tests/etl/verify_http_recovery.py --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/final-service-fault/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p test_verify_final_service_fault.py
```

Reproduce on a clean committed source with the standard hosting build/probes disabled and `qualify_http_recovery.py --parent BpmnModel --technical-fault --business-error-boundary matching --final-service-fault`, supplying the built owned-host/reader assemblies and a fresh output directory. Historical paths/ports are provenance, not reusable resources.
