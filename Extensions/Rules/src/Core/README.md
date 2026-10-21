# Elsa.Rules.Core

This module owns immutable finite-positive Rules values and language limits. A program carries its explicit vocabulary and rules; invocation facts, implementation pins, and native destinations are captured separately for one durable run. Collection-valued objects defensively snapshot their inputs, and artifact bytes are copied with a SHA-256 identity. Runtime verifies the digest against canonical artifact bytes and owns semantic validation and ordering.

Canonical serialization, evaluation, receipt verification, persistence, and native delivery proof belong to their respective Runtime, Persistence, and adapter modules.
