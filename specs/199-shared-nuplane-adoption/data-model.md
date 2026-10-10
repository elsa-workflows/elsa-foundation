# Data Model

No new persistent or host-coordination state is introduced.

| Concept | Owner | Constraint |
|---|---|---|
| Current integration policy | Standard options monitor + upstream per-delivery copy | Host-specific boolean fallback; changes invalidate cached default options through the real configuration token. |
| Pending freshness/reload epochs | Upstream CShells.Nuplane coordinator | Host code never duplicates them. |
| Per-shell reload outcome | Upstream registry; Elsa callback interprets results | Preserve original error chains, success counts, refusal warnings and sanitized operator details. |
| Root coordinator identity | Upstream private holder and participant/observer aliases | Borrowed observer in shells resolves the root object; no extra host coordinator or disposal owner. |

See [host contract](contracts/host-integration.md) and [research](research.md).
