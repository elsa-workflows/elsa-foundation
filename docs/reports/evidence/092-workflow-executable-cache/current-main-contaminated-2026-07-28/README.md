# Current-main diagnostic lane (not acceptance evidence)

These reports retain the first 200-request cache-on/off diagnostics after integrating current main.
Both used the same Release binary, coalesced checkpoint mode with segment cap 50, SQLite Groundwork,
an exact HTTP 200 / `Hello World!` synchronous workflow, and equivalent low-row-count database
snapshots. Both produced 663 physical checkpoint commits.

The host was not quiet: unrelated processes consumed more than three CPU cores during collection.
The results therefore document the integrated regression and preserve raw samples, but MUST NOT be
used as the final causal comparison or performance-acceptance lane.

| Lane | Warm p95 |
|---|---:|
| Cache off | 1,793.517 ms |
| Cache on (default) | 2,440.520 ms |

The required quiet-host 20-boot and 200-request acceptance runs remain open.
