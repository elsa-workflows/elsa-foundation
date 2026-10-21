# Elsa.Rules.Persistence.Core

This module contains provider-neutral Rules persistence contracts and bounded immutable request and result values. Commands return confirmations; queries return data. Implementations own transactions, provider mechanics, uniqueness and concurrency, while Runtime validates complete round semantics and the native adapter validates parent delivery proof.

The model revision tracks semantic state changes independently from worker and delivery lease fences. Accepted receipts and terminal results are immutable, and their bounded projections do not truncate history.
