#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.." 2>/dev/null || true
# Generated 2026-11-09 from the triage verdicts. Review before running.
gh issue edit 2226 --remove-label ready-for-agent $( [[ ' RFA todo kind:tests kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 EfSchemaChainStoreTests: upgrading a row four versions behin
gh issue edit 2229 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 A failed poison write after ack-on-fault loses the handler f
gh issue edit 2278 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 A leaked leftover activation's publication record stays Acti
gh issue edit 2279 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 A Studio rename on a Git Writer node is reverted at restart
gh issue edit 2280 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Git workflow export never pushes its version tags
gh issue edit 2287 --remove-label ready-for-agent $( [[ ' RFA todo kind:structure ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Renew root-write leases on their own DbContext, not the one 
gh issue edit 2313 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Recommended activity version stays on the old release after 
gh issue edit 2320 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Backfill completion is sometimes never recorded on main in a
gh issue edit 2321 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Fence the EF activation repair on the slot row, not only the
gh issue edit 2658 --remove-label ready-for-agent $( [[ ' RFA todo kind:perf auto-review ' == *' todo '* ]] || echo --add-label status:todo )  # P2 HttpEndpointMiddleware loads every waiting HttpEndpoint book
gh issue edit 2663 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P3 elsa-workbench image reports org.opencontainers.image.versio
gh issue edit 2723 --remove-label ready-for-agent $( [[ ' RFA todo kind:structure ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Delete dead code found by #2591 domain 1 (no production call
gh issue edit 2744 --remove-label ready-for-agent $( [[ ' RFA todo kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 VariableMapper.Map(IVariable) persists the variable's CLR ty
gh issue edit 2753 --remove-label ready-for-agent $( [[ ' RFA todo kind:structure ' == *' todo '* ]] || echo --add-label status:todo )  # P3 Feature classes discover handlers through GetType().Assembly
gh issue edit 2786 --remove-label ready-for-agent $( [[ ' RFA todo kind:api auto-review ' == *' todo '* ]] || echo --add-label status:todo )  # P3 DispatchWorkflowDeliveryOptions is configured but never read
gh issue edit 2787 --remove-label ready-for-agent $( [[ ' RFA todo kind:api auto-review ' == *' todo '* ]] || echo --add-label status:todo )  # P3 HTTP workflow base path has two option owners: configuring H
gh issue edit 3665 --remove-label ready-for-agent $( [[ ' RFA todo auto-qa kind:ux ' == *' todo '* ]] || echo --add-label status:todo )  # P3 Studio Structured Logs: prioritize message scanning beside s
gh issue edit 3666 --remove-label ready-for-agent $( [[ ' RFA todo auto-qa kind:ux ' == *' todo '* ]] || echo --add-label status:todo )  # P3 Studio: give active content more room in narrow authoring an
gh issue edit 3844 --remove-label ready-for-agent $( [[ ' RFA todo type:task kind:ux ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Complete User Tasks authoring, output selection and run diag
gh issue edit 3872 --remove-label ready-for-agent $( [[ ' RFA todo type:bug kind:bug ' == *' todo '* ]] || echo --add-label status:todo )  # P2 Open User Tasks in separate collection and detail views
gh issue edit 3876 --remove-label ready-for-agent $( [[ ' RFA todo type:feature kind:ux ' == *' todo '* ]] || echo --add-label status:todo )  # P3 Studio: separate and simplify Secrets creation and editing w
gh issue edit 3880 --remove-label ready-for-agent $( [[ ' RFA todo type:feature kind:ux ' == *' todo '* ]] || echo --add-label status:todo )  # P3 Make runtime Inputs compact and value-first
