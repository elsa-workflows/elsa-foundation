# Switch authoring, execution and conversion contract

The [requirements](../spec.md) govern behavior. Existing structures and ports are retained; no new endpoint or join contract is added.

## Authoring

The structure payload's existing `mode` is absent/null for MatchFirst and `MatchAny` for all matches; an explicit `MatchFirst` is normalized as today. Each case retains a stable `caseId`, human label `match`, optional condition and optional nested activity. Labels remain ordinally unique and condition inputs retain their normal type/binding validation. Mode changes preserve unknown payload members, nested activities, conditions and outcome wires.

Studio presents a labelled choice between “Match first” and “Match any” in the Switch Slots experience. Help explains selection order and Default in both modes, and independent bodies/all-body completion for MatchAny. Read-only views expose the selected mode without permitting edits. Existing standard controls and input syntax tooling remain the reference.

## Execution

For conditions true/false/true and labels A/B/C, MatchAny schedules populated A and C bodies and selects `[A, C]` only once both succeed. A selected empty case still contributes its label. No match selects `[Default]`, after its body if present. Normal child outcome names do not become Switch outcomes; Break is the existing explicit control exit and atomically cancels remaining child work. Switch has no Done outcome.

Predicate input failure starts no body. Child faults retain a blocking incident; cancellation is not success. Waits and process recovery preserve the committed selection and completed effects. A duplicate callback cannot add success. Outgoing connections fire only on Switch completion, with the existing default join when labels share a target.

## Conversion

Supported Elsa 3 MatchAny literal forms continue to lower to native MatchAny. Recursive body conversion retains the ordered cases, conditions, case identities and Default. The blanket populated-case refusal is removed after runtime/publication support exists. A Break without an intervening supported consuming loop wholly inside its case is refused with `ELS3-ACTIVITY-UNSUPPORTED-MODE` at its source location, even if a loop surrounds the Switch. This avoids converting Elsa 3's unhandled Break signal into native sibling cancellation. A supported case-local loop that consumes Break may convert under existing rules and must leave sibling work alive. Unknown/dynamic modes, malformed conditions and unsupported nested bodies retain diagnostics, paths and actionable guidance. Done mappings remain case-label/Default mappings; no compatibility mode or warning exception is introduced.

## Compatibility and versioning

Existing outcome-only MatchAny and MatchFirst artifacts keep their behavior. The current format already represents the required bodies and mode. Reuse its existing readers unless implementation evidence identifies an incompatible representation change, which must be recorded and reviewed before adoption. Any public constructor/API delta must be classified under the repository's SemVer rules and recorded in the PublicAPI files; a changed C# construction surface is separate from the activity structure schema.
