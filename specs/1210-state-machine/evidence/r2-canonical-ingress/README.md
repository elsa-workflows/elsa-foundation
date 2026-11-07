# Canonical stimulus ingress and retention evidence

This is component evidence for StateMachine M0, not complete StateMachine or native delivery acceptance.
The original Azure archives are retained byte for byte, including unsuccessful runs and their full logs.
[The manifest](manifest.json) records every archive's size, hash and distinct result.

The source baseline is `d9678af6f859cefa5f307f22f2a806857bf94a0e` with the exact reviewed patch
identified by manifest `b928d2226ce51038c994f3bbe73715928293584b91b6bb3ed61ab5a781edf47b`.
Publication at `3b54b8ab7` removes one extra EOF blank line from two files; the
[byte receipt](publication-whitespace.json) records both changes. Executable tokens are unchanged.
Later main integration and the dormant input pin/fingerprint component are separate sources, not
executions retroactively established by these archives.

| Qualification | Observed result |
|---|---|
| New canonical input/source/cohort identity cases | Runtime54 passed, zero failures/skips |
| Source installation, retention and races | EF19 passed, zero failures/skips |
| Schema and genuine legacy SQLite upgrade | 4 passed, zero failures/skips; original row digests preserved |
| Whole affected Runtime EF project | 1911 passed, zero failures/skips, in 883.82 seconds |
| Actual discovery reconciliation | 1911 rows across 973 methods; every method's case count equals execution; 1911 unique TRX test IDs and execution IDs |
| Binding identity causal control | 2 green; mutant1 expected `Assert.NotEqual` failure and1 pass; byte-restored2 green |
| Normal four-provider migration generation | Exit0; four migration/designer pairs and four snapshots |

The first EF19 run was 14 pass/5 fail. Root fencing increments the physical coordination revision;
a cached unchanged tracked entity then retained its previous revision. The source reload correction
preserves pending-change refusal and normal CAS/grant/expiry checks; the same 19 cases pass afterward.
An earlier namespace compilation failure, missing ignored genuine-fixture transfer, outdated finalization
test fixture and discovery-controller failure remain failed in their original archives.

The discovery controller initially demanded unique display names. xUnit truncates arguments, producing
two pairs of distinct tests with equal display names. That controller stopped before whole-suite
execution. The corrected reader preserves all 1911 rows and all method-count/failure/skip guards;
its successful run is a new, separately retained result.

The genuine baseline gzip hash is `3fcd6e38a968d05026a2c57a188f5d5c9d6fdbc313afd9ce19c82ad3e2b21416`;
its decompressed hash is `c62347ebb3cde510f07c2848eaf0b58c403c92ac605d23f78dfffcb167ea7540`.
No replacement fixture or skipped upgrade assertion was used. Its bytes are an existing qualification
input, not duplicated in these archives.

The 54-case Runtime proof reuse is limited to its unchanged 22-project Runtime test dependency closure:
the subsequent 14 provider/migration paths and one EF test-fixture argument do not enter that closure.
EF19 production and test methods are unchanged by the final schema-fixture correction. The whole EF
run then verifies the final 53-file patch against the real discovered inventory and unchanged compiled
inputs. Original phase receipts show no timeout, cancellation or remaining owned process groups in
successful runs; the whole-project unit ended inactive with a successful result.

These runs do not qualify live PostgreSQL, SQL Server or MySQL engines, full common source/work
participation or typed commits, native target admission/causal closure/control, physical upgraded-host
and cold restart journeys, the StateMachine activity/designer, Studio UX, saga composition, current-head
CI or resulting main. The producer remains disabled and the program remains incomplete.
