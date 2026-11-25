# Current native-work captures

These two complete real-host bundles were produced from clean source
`39260424a786fab0fe4b47dc7f8ae35af4696367`. Each retains all nine coupled SQLite
backups, the before/pending/finished observations, wire effects and process-loss
receipts. All fourteen actual writer products are pinned, including the canonical
and activity-schema hash writers. Packaging source `05be2cd3d3` independently
verifies the protocol histories and binds its five verifier/producer sources.

The retirement case preserves the original undelivered native start, cancellation
before admission, positive retirement and replay without native or ETL admission.
It makes no claim about a queued-start/admission race. The independently cancelled
worker case preserves the real HTTP 429 wait, the second process loss with ETL
cancellation and native result still owed, and recovery only after downstream
cancellation proof. Its actual workflow family writes version 2.1.0; historical
captures remain pinned to their original 2.0.0 predicate.

Both independent verifiers passed. Six current-reader replay/refusal controls
passed, retaining the missing-cancellation-source negative and repeated-start
checkpoint count, the independently cancelled worker's pending downstream refusal,
and precise unchanged-archive material-hash refusals. The original spec1017 bytes
remain unchanged. Earlier missing-Node, failed backup, incomplete writer-inventory
and stale verifier-count attempts remain in the local evidence ledger; none is
relabeled as a pass. Whole-suite and final-head review remain required.
