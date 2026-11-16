# Actionable provider diagnostics

**Status**: Implemented — effective when PR #3869 merges; acceptance remains gated by its evidence.

Issue: #3775, extended by the owner's publication screenshot on 5 November 2026. Program: Elsa 4 Simplification (#2559).

An author opening a blocked publication review sees what prevents publication, how to correct each problem, and a working action to the relevant editor field. Repeated discoveries of the same problem appear once. Technical provider identities and readiness details remain available without competing with corrections. Refresh after editing rechecks the exact saved revision. Test Run uses the same safe provider explanations.

Provider runtime text, exception details, subjects, paths and arbitrary metadata remain untrusted. Providers may explicitly declare static public diagnostic guidance at registration; guarded validation, dependency discovery and compilation use only this declared guidance for recognized codes. Unknown diagnostics retain a safe, honest fallback. Specialist configuration reports separate missing/invalid instructions, model reference, settings and execution limits, without echoing their values.

Acceptance: meaningful messages/actions through the guarded backend paths; canary protection for undeclared runtime text/metadata; specific specialist field focus, including advanced limits; duplicate suppression; blocked/ready/stale/error states; disabled publication with an accessible explanation; composed author/correct/review journeys, keyboard, light/dark and narrow layouts. Reference: module UI guidance and existing draft diagnostic navigation. No model invocation or external provider call is required for this proof.
