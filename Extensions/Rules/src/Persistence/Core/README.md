# Elsa.Rules.Persistence.Core

This module contains provider-neutral Rules persistence contracts and bounded immutable request and result values. Commands return confirmations; queries return data. Implementations own transactions, provider mechanics, uniqueness and concurrency, while Runtime validates complete round semantics and the native adapter validates parent delivery proof.

The model revision tracks semantic state changes independently from worker and delivery lease fences. Accepted receipts and terminal results are immutable, and their bounded projections do not truncate history.

`RuleRecordCodec` supplies bounded common framing and recovery readers for invocation, facts, receipts, terminal results and native delivery. It preserves opaque artifacts and semantic pins, checks canonical bytes and integrity, and does not infer rules. Unsupported common schemas throw `NotSupportedException`; corrupt or noncanonical records are rejected. Store implementations must retain full framed identity beside their indexed digest and distinguish unavailable readers from malformed data.

`IRuleAdmissionValidator` is the runtime's semantic admission seam. It must be called only after the provider checks for an existing identity; after validation the provider must recheck for a concurrent admission/cancellation winner, including after validation fails. The seam cannot authorize mutations by itself. Cancellation and existing-identity replay use common records without requiring the original interpreter. These ordering guarantees still require the concrete provider implementation and its tests.

`IRuleDeliveryProofValidator` is supplied by the native adapter. It verifies durable consumption or parent-terminal proof against the complete frozen delivery envelope. No validator means ProofUnavailable; an enqueue acknowledgement is insufficient. The store rechecks the durable winner even when validation fails. Canonical proof framing and full destination identity live in `RuleRecordCodec`; acknowledgement replay compares the exact retained proof and identifies the delivery obligation.
