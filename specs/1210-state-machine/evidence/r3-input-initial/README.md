# Initial invocation-input component run

The original run on published `dd36c153d78ff9484a1cd49e15d6370b6b8b4e26` is **FAILED**.
Its [inspection receipt](inspection.json) and byte-original archive preserve the full result.
The archive is 28,330 bytes with SHA-256
`773fbaad0a1bbd948e4004d6c01d1059c3d0f5730bf538189657fb8c682a99ee`.

Runtime semantic fingerprint cases: 21 executed, 21 passed, zero failures/skips.
The pin target expected 11 cases but executed 9: 8 passed and 1 failed. Malformed strings
passed as attribute data become replacement characters before the test receives them.
Three distinct intended invalid-code-unit cases therefore collapsed into one display/data row;
the production validator correctly received valid replacement characters instead.
The corrected fixture passes integer code units and constructs the strings inside the test.
No production pin-validation change was needed.

Separately, source review found that semantic depth 64 was rejected after adding a fingerprint
envelope through the default JSON serializer. Direct canonical envelope writing removes that
extra limit; exact 64 and over-limit 65 controls cover the correction. Historical fingerprint
paths are unchanged. The corrected 57-case source is not qualified by this original run.

Both original phases completed without timeout or remaining owned process groups. These are
dormant pin/fingerprint components; no native input admission, physical receiver persistence,
StateMachine activity, designer or completed feature is claimed.
