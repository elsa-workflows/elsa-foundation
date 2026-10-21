# Elsa.Rules.Runtime

This implementation checkpoint supplies version-one canonical artifact encoding, bounded finite-positive round evaluation and complete receipt validation. Every rule reads the same frozen input; a receipt includes all applicable firings, including conclusions already present. Newly derived facts become input only after a durable consumption transition.

The evaluator is internal and implements the provider-neutral `IRuleRoundValidator`. Structural value validation is shared within the Rules domain; this does not expose an implementation helper as public API. Physical attempt attribution is outside the semantic receipt digest. Malformed input and unsupported schemas have distinct validation outcomes.

Common record encoding and recovery readers live in `Rules.Persistence.Core`, keeping providers independent of Runtime. Finite admission capacity counts complete receipt/result records and native delivery copies; provider enforcement and trusted installed-package evidence are still required.

The internal admission validator compares the invocation pin with `IRuleImplementationIdentitySource` before interpreting finite semantics. A missing or incompatible deployment returns Unavailable. Malformed common data returns Invalid; intact but invalid finite input returns Refused. No default identity source is supplied yet: synthetic unit-test pins do not qualify deployed packages.

Worker/facade registration, durable stores, trusted deployed-package identity and native invocation are still under construction in [spec 999](../../../../specs/999-durable-rules/tasks.md). The focused semantic tests are not package or distributed-process qualification. No default feature is advertised until it composes an actual runnable service.
