# Opt-in composition inspection CLI v1

Proposed command; not implemented by specification PR2175.

```text
dotnet elsa composition inspect
  --host <installed-framework-dependent-host-output>
  --host-dir <configuration-source-directory>
  --shell <id> --environment <name>
  --composition <accepted-composition.json> [--catalog <catalog.json>]
  [--workspace-profile <profile.json>]... [--setting-review <review.json>]
  [--packages <installed-package-root>]... --trust-host-code
  [--timeout-seconds <1..300>] [--format <text|json>]
```

`--host` selects assembly closure; `--host-dir` selects file inputs. They may be different directories. Host assembly validation uses the compiled layout, never a candidate configuration directory. Existing import/plan/generate/init/accept remain file-only and do not call this operation. No restore, connection-value/env/stdin, migration, output-directory, save, resource/module-filter or automatic-accept option is accepted here. All candidate consumers are inspected. Default timeout60s.

The explicit command and required trust flag acknowledge that the selected installed host's declared composer executes in a child process. No interactive confirmation is added to inspection. Missing trust refuses before process creation. Package roots must already be installed; absence/ambiguity/failure refuses without acquisition. The Elsa-owned operation loads metadata/composer only, does not start a host, construct feature services or touch a database. Arbitrary trusted composer code is not sandboxed, so no claim guarantees absence of side effects in malicious/custom code.

Catalog is optional, matching init/accept/plan/generate. Without --catalog, resolve the authored pin using immutable bundled FoundationSelectionCatalog.LoadFor; never substitute the latest catalog. Capture/recheck an explicit catalog file only when supplied. Capture the full SourceSnapshot and all supplied composition input files once. Parse pins through existing strict readers, invoke CompositionCandidateBuilder.Build once, and require accepted expansion. Keep unused workspace profiles in drift checks. Candidate Build uses supported object-map edits and reviewed existing setting/resource paths, preserving existing refusal boundaries. Every explicit Remove gets a selected-overlay false declaration even if absent from the file-enabled set; generation uses the same builder behavior. Where a source shape cannot represent this without losing local data, refuse rather than reinterpret it.

Before launch and immediately before final output, recheck all captured inputs. The final check also precedes a returned host refusal or thrown nonfatal process/rendering refusal, after the worker's owned-cleanup path; a newer input-read/drift refusal takes precedence over a stale bounded outcome. Fatal trust exceptions retain their propagation boundary. The host resolves the post-edit candidate bytes, not pre-edit files. No temporary candidate directory, source write or publication. A later generate invocation has its own fresh capture/review; preview is not authorization or cross-invocation evidence.

Human stdout and JSON derive from one validated result and contain safe exact selection/reasons plus a separate configurationResolution projection. Do not set Planning.PersistenceEvidence.Status=checked: its broader physical/schema/migration evidence is not supplied here. CLI strips private correlation tokens and file entries. Public input errors use existing codes where safe, but fixed messages and no source path, arbitrary rationale, raw excerpt or exception text. Progress/refusal text uses stderr; no partial JSON, raw host stdout/stderr or secret-bearing hashes.

JSON output contains `plan` (the existing safe composition-plan projection) and `configurationResolution` (the validated public host projection). It does not expose the private host or worker envelope. Human output renders those same two projections.

| Outcome | Exit | Output |
|---|---:|---|
| Complete valid configuration projection, including explicit unavailable legacy/provenance/live checks | 0 | One safe human or JSON preview. Resolved configuration is not runtime readiness. |
| Invalid/stale/unaccepted intent, unsupported source shape, trust missing, selection or existing EF configuration conflict, malformed/oversized request | 2 | No preview; fixed safe refusal code/message. |
| Unreadable/drifted input, host/package/capability unavailable, invalid/oversized response, process failure/timeout | 3 | No preview; fixed safe refusal. |
| User cancellation | 2 | No preview; candidate-inspection-cancelled after owned child cleanup. |

New operation codes: candidate-trust-required; candidate-selection-conflict (reason enum: unknown, unavailable, requested-extra, requested-missing, expanded-extra, required-disabled, case-collision); candidate-capture-invalid; candidate-request-invalid; candidate-request-too-large; candidate-capability-unavailable; candidate-response-invalid; candidate-response-too-large; candidate-inspection-timeout; candidate-inspection-cancelled; candidate-host-unavailable; candidate-closure-changed; candidate-package-unavailable; candidate-inspection-failed; candidate-cleanup-failed. `candidate-closure-changed` is a pre-host outer refusal for observed installed metadata drift, with no preview (exit3). Existing safe bridge/composition refusal codes are retained for input/build/drift errors. Existing EF refusal codes are enumerated in the worker contract. No raw strings from a peer error become user messages.
