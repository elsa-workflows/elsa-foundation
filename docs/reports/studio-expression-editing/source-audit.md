# Independent source audit: expression editing

Read-only audit performed on 2026-10-02 against Foundation Studio commit `69acb2ab6a517249cc3442adbcf93ff185c987b9`. The root investigator reviewed the findings alongside source and computer-use evidence. This is an investigation result, not an automated-test pass.

Tracked source roots are `src/apps`, `src/essentials` and `src/extensions`; pre-existing untracked `src/Elsa.Studio.*` output directories were excluded. The source already implements:

- CodeMirror 6 with JavaScript/Liquid language packages, lazy grammar loading, highlighting, completion, lint, commands, history and editor state.
- Automatic and explicit completion, pointer/keyboard hover and signature help, with optional Foundation `expressions.tooling.v1` context/catalog/completion/hover/validation relations.
- Compact activation of one rich editor, scoped editor sessions, and multiline expansion. The SDK provides separate inline/expanded surfaces and per-Contribution tooling capabilities.
- Explicit unavailable/incompatible/unauthorized tooling treatment, local syntax-aware editing, and component/inspector/browser regression fixtures.

Concrete gaps found:

1. Syntax coloring uses CodeMirror's `defaultHighlightStyle`. The theme prop becomes a data attribute; no Studio-token-based syntax palette is installed. Surface CSS alone does not prove light/dark/dim syntax contrast.
2. Signature help selects the first signature and displays a label plus optional documentation. Active-parameter highlighting and overload navigation are absent.
3. Formatting is explicitly advertised as unsupported by both language Contributions.
4. Local syntax error text is generic (`Syntax error.`), although error locations are available.
5. JavaScript grammar enables JSX and TypeScript; runtime compatibility requires reconciliation rather than assuming those constructs execute.

The existing feature verification record was reconciled on 2026-07-28. Those results are historical; the auditor did not rerun them. Root investigation subsequently found the normal-host discovery fix in Studio #546 and the remaining backend/validation/Liquid-context gaps, described in the [canonical assessment](assessment.md). Source references, browser evidence and the delivery proposal are there; this record preserves the independent audit's evidence boundary.
