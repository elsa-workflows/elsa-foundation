#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.." 2>/dev/null || true
# Generated 2026-11-09 from the triage verdicts. Review before running.
gh issue edit 2736 --remove-label status:parked --add-label status:todo  # registry names it as BPMN Engine open work
gh issue edit 1127 --remove-label blocked  # blocked label came from machine load, not a real blocker
gh issue edit 2504 --remove-label blocked  # M2 landed; no longer blocked
gh issue edit 2777 --remove-label blocked  # blocker #2774 closed
gh issue edit 2778 --remove-label blocked  # blocker #2773 merged
gh issue edit 2782 --remove-label blocked  # blocker #2773 merged
gh issue edit 2761 --remove-label status:in-progress --add-label status:todo  # in progress 26 days with no PR
gh issue edit 3815 --remove-label status:todo --add-label status:in-progress  # has active PR #3840
gh issue edit 3820 --remove-label status:todo --add-label status:in-progress  # has active PR #3840
gh issue edit 3525 --add-label status:todo  # no status label
gh issue edit 1152 --add-label elsa3-parity  # and make it a sub-issue of #2771 in the UI
