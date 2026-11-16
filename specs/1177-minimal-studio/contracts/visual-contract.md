# Minimal Studio visual contract

Approved images in `../references/` define direction. These measurements normalize small generated inconsistencies. Real data, labels, permissions, counts and pagination remain truthful.

| Preset | Stable ID | Reference mode | Navigation / workspace | Small accent |
|---|---|---|---|---|
| Porcelain | builtin:porcelain | Light | light stone / warm white | slate |
| Atelier | builtin:atelier | Light | graphite / near white | sage |
| Graphite | builtin:graphite | Dark | deep charcoal / lifted charcoal | mist sage |

Primary actions are neutral with paired readable foregrounds. Focus/selection marks use the small accent. Ordinary hover, successful nodes and status surfaces avoid broad color washes. Semantic warning/error/runtime information remains clear through text and icons.

## Measurements at 1440px

| Role | Target |
|---|---|
| Expanded navigation | 216px; compact/top arrangements remain supported |
| Content gutter | 24–28px; 16px narrow |
| Page title | 28px, sans serif, weight600; embedded header stays compact |
| Body / label | 14px / 12px, readable line height |
| Standard control | 36px minimum; compact variants stay compact |
| Collection row | 52–56px minimum, growing with content |
| Control / table / dialog radius | approximately 8px / 12px / 16px |
| Section spacing | 24px, shared 4px internal rhythm |
| Row boundary | fine 1px dotted neutral rule; no vertical grid |
| Focus boundary | solid visible 2px ring |

## Surface and state rules

- One page title/short description and at most one filled primary header action.
- Names lead; identifiers stay secondary/copyable; stable overflow, selection and truthful paging.
- Fine outer card boundaries, restrained depth, no decorative box around every nested value.
- Separate Built-in starters from Your templates. Keep builtin read-only behavior and truthful content previews.
- Inspectors retain tabs, occurrence navigation, evidence values and technical disclosure. Runtime views stay read-only.
- Logs retain filters, stream state, selection, details and visible warnings.
- Dots decorate row/secondary separation and meaningful grouping/add areas, never focus. High contrast/forced colors strengthen boundaries as needed.
- Populated data and its empty-state inset are not simultaneous application states.
- Narrow containment, keyboard, loading/empty/error/unsupported states and reduced motion are part of acceptance.

## Visual review

Capture definitions in all three reference combinations, then runs, Appearance, run inspection, templates and logs. Compare actual rendered type, geometry, spacing and navigation contrast at matching viewports. Record real screenshot paths and deviations in PR evidence. A standalone public module must receive the same contract.
