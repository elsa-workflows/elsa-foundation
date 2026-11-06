# Bounded DMN interchange and differential witness

The committed fixture demonstrates actual editing in both directions and 56 finite evaluator cases. All 56 Elsa expectations passed on engine/editor source `559af440d039f3b7af761566fe77e36ea888a256`; 27 reference outcomes were equivalent and 29 differed or fell outside the selected profile. These counts describe this fixture, not a DMN conformance level or TCK result. The independent engine is an investigative development dependency, never a product dependency or correctness oracle.

## Pinned tools and licenses

| Tool | Exact identity | Use and license |
| --- | --- | --- |
| dmn-js | 18.0.1, upstream commit [`feddee69ded44a62324cee04fe162aa73b9a1395`](https://github.com/bpmn-io/dmn-js/tree/feddee69ded44a62324cee04fe162aa73b9a1395) | Real browser import, modeling commands, export and reimport. bpmn.io license includes a visible-watermark requirement; its rendering code was not removed, hidden or changed. Full notice in `licenses/dmn-js.txt`. |
| dmn-moddle | 12.2.1, upstream commit [`75e2f759fa3e168a2a098358825ede2a0de02282`](https://github.com/bpmn-io/dmn-moddle/tree/75e2f759fa3e168a2a098358825ede2a0de02282) | External editor's DMN model/XML support, MIT. Full notice retained. |
| ScratchyDisk.DmnEngine | NuGet 2.0.1, package repository commit [`67bae6927a28f77138aae98dcd35d93b1af39838`](https://github.com/Scratchydisk/ScratchyDisk.DmnEngine/tree/67bae6927a28f77138aae98dcd35d93b1af39838) | Independent .NET reference evaluator. Package declares MIT; source notice also attributes Common.DMN.Engine. Full notice in `licenses/ScratchyDisk.DmnEngine.txt`. Runtime dependencies pinned in the lock file: Antlr4.Runtime.Standard 4.13.1 and NLog 5.3.4. |
| Playwright / esbuild | 1.56.1 / 0.25.11 | Browser driver / bundling the actual Studio utility. Apache-2.0 / MIT. Exact dependency tree and integrity hashes in `browser/package-lock.json`. |
| Chromium | 156.0.8078.80 | Actual locally available browser used for capture; version is recorded, no browser binary vendored. |

No Java runtime, server deployment or container was needed. No third-party implementation or binary is vendored here. Lock files record the executed dependency envelope; license notices are retained for the two important upstream components and model parser. This witness does not qualify the reference engine to execute untrusted documents or claim its broader FEEL grammar is bounded.

## Actual edits and preservation

`browser/roundtrip.cjs` bundles the owning Studio `decisionXml.ts` directly. `manifest.json` and `roundtrip/receipt.json` record the exact file/bundle hashes. No substitute XML editor was used.

1. Studio's real `newDecisionXml` creates `roundtrip/product.dmn`; the real native `editTable` changes its condition to `<= 1000`. dmn-js imports the table and its actual modeling service changes the output to `Externally approved`, then saves `forward.dmn`. Studio's parser reads that edited output. Elsa evaluates the exported XML at the boundary and above it.
2. `external.dmn` includes an external table, DMNDI shape/bounds, documentation and stable IDs. dmn-js actually edits its output and exports `externalEdited.dmn`. Studio's real `editTable` changes that cell to `Studio approved`, producing `nativeEdited.dmn`. dmn-js reimports and saves `reverse.dmn`. Assertions verify the changed value, unchanged IDs and covered diagram/documentation across native editing and external reimport. Elsa evaluates the edited/reimported XML, and both forms have identical canonical executable hashes.
3. `opaque.dmn` adds one unknown extension subtree. Studio changes an owned cell and keeps that subtree (`opaqueEdited.dmn`); dmn-js imports, edits a different owned cell and exports it again (`opaqueExternal.dmn`), keeping the subtree. All three are explicitly refused by the default compiler: preservation/viewability does not certify unknown semantics as executable.

All external imports in these covered paths reported zero warnings. The modelers were destroyed and left no mounted editor instances. The two causal controls replace the external edit or omit the native edit in a private harness copy: each fails the corresponding changed-value assertion. The red logs are retained. No shared product source was mutated by those controls.

Covered preservation means the listed IDs, DMNDI reference/bounds, documentation and specific opaque namespace/key/text subtree. Formatting/prefixes and source hashes may change after editing. Arbitrary vendor extensions, arbitrary diagrams and full editor compatibility have not been established. Original-byte retention and authenticated HTTP export are separate lifecycle/host evidence, not claims made by this utility fixture.

## Expectations and actual disagreements

The normative basis is [OMG DMN 1.3](https://www.omg.org/spec/DMN/1.3/PDF): §10.3.1.2 rules 7–15 (printed p.99) and rule 64 (p.101), §10.3.2.10 (pp.114–116), tables 50/51 (pp.121–122), and table 55 (p.124). The selected closed profile is [the profile contract](../../contracts/dmn-profile.md). Its narrower grammar, numeric scale bounds and explicit refusals are profile choices. Expectations live independently in `cases.json`; `differential-results.json` records expected, actual Elsa, actual reference, trace, identities, raw reference CLR types and errors for every case.

| Area | Actual outcome |
| --- | --- |
| Approval boundaries, five selected policies, equal ANY numeric values, supported ranges/alternatives, several null cases, ordinary escapes, exact >2^53 values and standard unnamed output | 27 reference results match after unwrapping the reference's result-variable wrappers. COLLECT comparisons use a multiset with multiplicities. |
| No matching row | Elsa returns NoMatch with tagged null. Five reference cases return a native empty result list. That shape difference remains visible. |
| Wildcard with null | Elsa does not match, following §10.3.2.10's no-inputValues rule. The reference matches. |
| `not(< 10)` with null | Elsa preserves unknown and does not match. The reference matches. This disagreement is recorded against ternary negation and comparison semantics. |
| Unicode code-point / escaped apostrophe literals | Elsa decodes the profile's FEEL escapes; the reference refuses the two tested expressions. |
| Multiple named outputs | Elsa retains `exact amount` as an exact key; the reference normalizes it to `exact_amount`. Typed values otherwise remain visible. |
| 34-digit coefficient and large power of ten | Elsa returns the exact supported values. The reference refuses those literals; successful reference decimal values are rendered as exact plain decimal text without binary conversion. |
| Broader grammar/policies | Elsa refuses functions, references, conditionals, PRIORITY, aggregation, leading equality shorthand and >34-digit coefficients. Actual reference execution or errors are recorded, without interpreting acceptance as approval to widen the profile. |
| Editor-produced tables without optional inputData declarations | Elsa compiles/evaluates them. The reference refuses input assignment because it does not expose those symbols as input parameters. XML was not rewritten to hide this interoperability limit. Unknown-extension round trips reach the same reference input-assignment limit; no claim about safe reference extension execution follows. |

DMN 1.3 §8.3.2 table 34 (printed p.77) forbids naming a single output. The engine/editor now support the standard omitted-name form and canonical executable XML omits that unused name. Common editor exports with a bounded single-output name are also accepted as the profile's documented interoperability relaxation. Multiple-output names remain required and exact. This is another reason to avoid a strict whole-document conformance claim.

## Reproduce outside the checkout

Requires .NET 10, Node/npm and a Chromium executable. The normal `dotnet` build-slot wrapper remains on PATH. Set `CHROMIUM_EXECUTABLE` to an installed browser, or install the pinned Playwright browser first. From the repository root:

```sh
CHROMIUM_EXECUTABLE=/absolute/path/to/chromium python3 specs/1195-business-decisions/evidence/interop/run.py --output /absolute/new/private-directory
```

The script refuses to overwrite an existing output or write generated dependencies inside the checkout. It copies this fixture, runs `npm ci --ignore-scripts`, executes the actual browser mutations, restores the pinned reference project with `--locked-mode`, and runs the finite matrix against the checkout's actual engine. A clean private reproduction passed all 56 cases. Newly generated Studio IDs vary; compare behavior, covered identity preservation within each run and source/tool hashes instead of expecting random IDs to match earlier captures. A changed product source or browser is a new evidence identity.

## Imported declaration mutation regression

The actual browser-bundled Studio utility additionally produces five documents checked by the real compiler/evaluator in `editor-mutations.json`: input rename, input type change, output scalar type change, scalar-to-record output shape and unused-symbol generation after a rename. All five compile and evaluate successfully with exact expected results. These are editor/engine integration checks, separate from the 56-case reference differential. A declaration-backed input deletion explicitly refuses before producing changed XML, preserving its references/diagrams for editing in a DMN modeler. The unit control fails four tests with the earlier utility and passes the complete 40-test module suite after restoration.
