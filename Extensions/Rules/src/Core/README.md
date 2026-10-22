# Elsa.Rules.Core

This module owns immutable finite-positive Rules values and language limits. A program carries its explicit vocabulary and rules; invocation facts, implementation pins, and native destinations are captured separately for one durable run. Collection-valued objects defensively snapshot their inputs, and artifact bytes are copied with a SHA-256 identity. Runtime verifies the digest against canonical artifact bytes and owns semantic validation and ordering.

Canonical serialization, evaluation, receipt verification, persistence, and native delivery proof belong to their respective Runtime, Persistence, and adapter modules.

`IRuleInvocationFactory` is the direct preparation contract. It returns one frozen envelope or a bounded Invalid/Unavailable outcome without writing state. Its implementation belongs to Runtime; store admission and result inspection remain separate persistence contracts. Retries reuse the original prepared envelope, including its captured budget and package pin.

`IRuleArtifactCompiler` compiles reusable program content without selecting a deployment or creating a run. It returns the canonical immutable artifact and validated vocabulary count. Native publication uses that count to freeze a later invocation's default budget without interpreting the artifact again.
