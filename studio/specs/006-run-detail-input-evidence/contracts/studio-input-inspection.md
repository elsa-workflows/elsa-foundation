# Contract: Studio Input Inspection and Expression Rendering

## Row derivation

```ts
type InputInspectionRow = {
  rowKey: string;
  inputKey?: string;
  name: string;
  declaredType?: string;
  declaration?: DeclaredActivityInput;
  source?: ExecutableInputSource;
  evaluations: InputEvaluationEvidence[];
  latestEvaluation?: InputEvaluationEvidence;
  states: InputInspectionState[];
};
```

`buildInputInspectionRows` is a pure, linear-time function. It consumes declared inputs, pinned sources, and evidence, never a mutable draft and never the legacy summary.

## Expression renderer contribution

```ts
type ExpressionSourceRenderContext = {
  expressionType: string;
  value: unknown;
  metadata: Readonly<Record<string, unknown>>;
  isSensitive: boolean;
};

type StudioExpressionSourceRenderer = {
  compact(context: ExpressionSourceRenderContext): React.ReactNode;
  expanded(context: ExpressionSourceRenderContext): React.ReactNode;
};

// Additive member of the existing public SDK contribution.
type StudioExpressionEditorContribution = {
  // existing id/order/supports/surfaces/metadata members...
  sourceRenderer?: StudioExpressionSourceRenderer;
};
```

The exact repository interface may adapt to existing contribution conventions, but these semantics are required:

- Reuse existing contribution `id`, `order`, `supports`, admission, and Host Policy; source selection calls `supports` with a read-only source context.
- Expression-owned modules register semantic renderers through their existing public SDK contribution.
- Workflows invokes the renderer only for authorized, non-redacted source.
- A generic renderer always displays type plus safe text or verbatim opaque JSON.
- Renderer failure is isolated and falls back generically.

## Presentation contract

- Compact row: name/type, “Evaluated at runtime” evidence summary/status, source-kind badge, safe source preview, anomaly/protected badges.
- Expanded runtime section: latest bounded snapshot, evidence state/mode/provenance, phase/sequence/time, safe failure; prior evaluations in collapsed history.
- Expanded source section: authored source first; compiled binding only when different or additionally informative.
- Outputs remain in the existing runtime-only evidence presentation.
- Activity content order: summary → Inputs → Outputs → collapsed Execution details.

## Accessibility

- Row disclosure uses a button with `aria-expanded` and a stable controlled region.
- Runtime and source sections have headings and independent protected-state text.
- History is an ordered list/table with phase, sequence, and time labels.
- Status is conveyed by text and semantics, not color alone.
- Overlay/focus modes trap or transfer focus only while active and restore it on close.
