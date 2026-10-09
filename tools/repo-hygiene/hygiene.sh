#!/usr/bin/env bash
# Repository hygiene: weekly branch sweep plus a report on branch and issue health.
# Policy and thresholds: docs/contributing/repository-hygiene.md
#
# Usage: hygiene.sh [report|enforce]
#   report   (default) lists what the sweep would delete and writes the report. Changes nothing.
#   enforce  tags each swept branch tip as archive/<branch>, then deletes the branch.
#
# Needs git with the full history of origin, an authenticated gh (GH_TOKEN) and jq.
# Environment:
#   GITHUB_REPOSITORY   owner/name (set by Actions)
#   STALE_BRANCH_DAYS   idle days before a branch without an open PR is swept (default 14)
#   REPORT_FILE         where to write the Markdown report (default: a temp file)
#   POST_REPORT=true    comment the report on the open issue labelled repo-hygiene
set -euo pipefail

mode="${1:-report}"
case "$mode" in report|enforce) ;; *) echo "usage: $0 [report|enforce]" >&2; exit 2 ;; esac

repo="${GITHUB_REPOSITORY:?set GITHUB_REPOSITORY=owner/name}"
owner="${repo%%/*}"
stale_days="${STALE_BRANCH_DAYS:-14}"
report="${REPORT_FILE:-$(mktemp)}"
now=$(date -u +%s)
day=86400

# Never swept, in addition to branches GitHub marks protected.
exempt_re='^(main|publish-state|release/.*|rc/.*)$'

# Issue thresholds; keep in step with the policy doc.
max_programs=2
max_ready_for_agent=20
triage_days=14
ready_idle_days=30
in_progress_idle_days=14

since() { date -u -d "@$(( now - $1 * day ))" +%Y-%m-%dT%H:%M:%SZ; }

# Walks a list endpoint page by page and applies a jq filter to each item.
# (Explicit paging rather than gh --paginate, whose next links use numeric repository ids.)
list_all() { # url-with-query jq-filter
  local page=1 body
  while :; do
    body=$(gh api "$1&per_page=100&page=$page")
    [[ $(jq length <<<"$body") == 0 ]] && break
    jq -c ".[] | $2" <<<"$body"
    page=$((page + 1))
  done
}

# --- Branch sweep ---------------------------------------------------------------------------

git fetch --quiet --prune origin '+refs/heads/*:refs/remotes/origin/*'
main_tree=$(git rev-parse 'origin/main^{tree}')
protected=$(gh api "repos/$repo/branches?protected=true&per_page=100" --jq '.[].name')

swept=()
kept=()
total_branches=0
while read -r sha ts branch; do
  [[ "$branch" == HEAD ]] && continue
  total_branches=$((total_branches + 1))
  if [[ "$branch" =~ $exempt_re ]] || grep -qxF "$branch" <<<"$protected"; then continue; fi

  prs=$(gh api -X GET "repos/$repo/pulls" -f state=all -f head="$owner:$branch" -F per_page=100 \
    --jq '[.[] | {number, state, merged: (.merged_at != null), sha: .head.sha}]')
  open_pr=$(jq -r '[.[] | select(.state == "open") | .number] | first // empty' <<<"$prs")
  if [[ -n "$open_pr" ]]; then
    kept+=("\`$branch\` | open PR #$open_pr")
    continue
  fi

  merged_pr=$(jq -r --arg s "$sha" '[.[] | select(.merged and .sha == $s) | .number] | first // empty' <<<"$prs")
  age=$(( (now - ts) / day ))
  if [[ $(git rev-list --count "origin/main..$sha") == 0 ]]; then
    reason="contained in main"
  elif [[ -n "$merged_pr" ]]; then
    reason="tip merged as PR #$merged_pr"
  elif tree=$(git merge-tree --write-tree origin/main "$sha" 2>/dev/null) && [[ "${tree%%$'\n'*}" == "$main_tree" ]]; then
    # Merging it would not change main: a squash merge or cherry-pick already carried the work.
    reason="changes already in main"
  elif (( age >= stale_days )); then
    reason="no open PR, idle ${age}d"
  else
    kept+=("\`$branch\` | no open PR, idle ${age}d (sweep at ${stale_days}d)")
    continue
  fi
  swept+=("$branch|$sha|$reason")
done < <(git for-each-ref refs/remotes/origin --format='%(objectname) %(committerdate:unix) %(refname:lstrip=3)')

deleted=0
if [[ "$mode" == enforce ]]; then
  for row in "${swept[@]}"; do
    IFS='|' read -r branch sha _ <<<"$row"
    tag="archive/$branch"
    existing=$(git ls-remote origin "refs/tags/$tag" | cut -f1)
    if [[ -n "$existing" && "$existing" != "$sha" ]]; then tag="$tag-${sha:0:12}"; fi
    git push --quiet origin "$sha:refs/tags/$tag"
    # The lease refuses the delete if someone pushed to the branch after it was inspected.
    if git push --quiet --force-with-lease="refs/heads/$branch:$sha" origin ":refs/heads/$branch"; then
      deleted=$((deleted + 1))
    else
      echo "::warning::kept $branch: it moved after inspection"
    fi
  done
fi

# --- Issue health ---------------------------------------------------------------------------

open_issues=$(list_all "repos/$repo/issues?state=open" \
  'select(.pull_request == null) | {number, title, created_at, updated_at, labels: [.labels[].name]}' | jq -s .)
recent=$(list_all "repos/$repo/issues?state=all&since=$(since 7)" \
  'select(.pull_request == null) | {created_at, closed_at}' | jq -s .)
open_prs=$(list_all "repos/$repo/pulls?state=open" '.number' | wc -l)

week_ago=$(since 7)
created_7d=$(jq --arg t "$week_ago" '[.[] | select(.created_at >= $t)] | length' <<<"$recent")
closed_7d=$(jq --arg t "$week_ago" '[.[] | select(.closed_at != null and .closed_at >= $t)] | length' <<<"$recent")

# Selects open issues matching a jq filter; prints "count<TAB>bullet list (first 25)".
select_issues() {
  jq -r "[.[] | select($1)] | \"\(length)\t\" + ([.[:25][] | \"- #\(.number) \(.title)\"] | join(\"\n\"))" <<<"$open_issues"
}
has() { printf '(.labels | index("%s"))' "$1"; }
structural="($(has type:program) or $(has type:epic))"
triage_labels="($(has needs-triage) or $(has needs-info) or $(has ready-for-agent) or $(has ready-for-human) or any(.labels[]; startswith(\"status:\")))"

IFS=$'\t' read -r -d '' n_programs l_programs < <(select_issues "$(has type:program)"; printf '\0') || true
IFS=$'\t' read -r -d '' n_ready l_ready < <(select_issues "$(has ready-for-agent)"; printf '\0') || true
IFS=$'\t' read -r -d '' n_untriaged l_untriaged < <(select_issues "$(has needs-triage) and .created_at < \"$(since $triage_days)\""; printf '\0') || true
IFS=$'\t' read -r -d '' n_ready_idle l_ready_idle < <(select_issues "$(has ready-for-agent) and .updated_at < \"$(since $ready_idle_days)\""; printf '\0') || true
IFS=$'\t' read -r -d '' n_wip_idle l_wip_idle < <(select_issues "$(has status:in-progress) and .updated_at < \"$(since $in_progress_idle_days)\""; printf '\0') || true
IFS=$'\t' read -r -d '' n_unlabelled l_unlabelled < <(select_issues "($triage_labels | not) and ($structural | not)"; printf '\0') || true

mark() { if (( $1 > $2 )); then echo "⚠️"; else echo "✅"; fi; }
section() { # title count list
  (( $2 == 0 )) && return
  printf '\n<details><summary>%s (%s)</summary>\n\n%s\n\n</details>\n' "$1" "$2" "$3"
}

{
  echo "## Repository hygiene — $(date -u +%Y-%m-%d) ($mode)"
  echo
  echo "| Signal | Value | Target |"
  echo "|---|---|---|"
  echo "| Open issues | $(jq length <<<"$open_issues") | < 100 |"
  echo "| Issues opened / closed, last 7 days | $created_7d / $closed_7d | closed ≥ opened |"
  echo "| Open programs | $n_programs $(mark "$n_programs" $max_programs) | ≤ $max_programs |"
  echo "| \`ready-for-agent\` buffer | $n_ready $(mark "$n_ready" $max_ready_for_agent) | ≤ $max_ready_for_agent |"
  echo "| \`needs-triage\` older than ${triage_days}d | $n_untriaged $(mark "$n_untriaged" 0) | 0 |"
  echo "| \`ready-for-agent\` idle ≥ ${ready_idle_days}d | $n_ready_idle $(mark "$n_ready_idle" 0) | 0 |"
  echo "| \`status:in-progress\` idle ≥ ${in_progress_idle_days}d | $n_wip_idle $(mark "$n_wip_idle" 0) | 0 |"
  echo "| Issues without a triage label | $n_unlabelled $(mark "$n_unlabelled" 0) | 0 |"
  echo "| Branches / open PRs | $total_branches / $open_prs | branches ≈ open PRs + exempt |"
  if [[ "$mode" == enforce ]]; then
    echo "| Branches archived and deleted | $deleted of ${#swept[@]} | |"
  else
    echo "| Branches the sweep would delete | ${#swept[@]} | |"
  fi

  section "Open programs" "$n_programs" "$l_programs"
  section "Needs triage for more than ${triage_days} days" "$n_untriaged" "$l_untriaged"
  section "Ready for an agent but idle for ${ready_idle_days}+ days: re-triage or close" "$n_ready_idle" "$l_ready_idle"
  section "In progress but idle for ${in_progress_idle_days}+ days" "$n_wip_idle" "$l_wip_idle"
  section "Without a triage label" "$n_unlabelled" "$l_unlabelled"
  if (( ${#swept[@]} > 0 )); then
    printf '\n<details><summary>Swept branches (%s)</summary>\n\n| Branch | Tip | Reason |\n|---|---|---|\n' "${#swept[@]}"
    for row in "${swept[@]}"; do IFS='|' read -r b s r <<<"$row"; echo "| \`$b\` | \`${s:0:10}\` | $r |"; done
    printf '\n</details>\n'
  fi
  if (( ${#kept[@]} > 0 )); then
    printf '\n<details><summary>Retained branches (%s)</summary>\n\n| Branch | Why |\n|---|---|\n' "${#kept[@]}"
    for row in "${kept[@]}"; do echo "| $row |"; done
    printf '\n</details>\n'
  fi
  echo
  echo "Policy: [repository hygiene](${GITHUB_SERVER_URL:-https://github.com}/$repo/blob/main/docs/contributing/repository-hygiene.md). Restore an archived branch with \`git push origin archive/<branch>:refs/heads/<branch>\`."
} > "$report"

[[ -n "${GITHUB_STEP_SUMMARY:-}" ]] && cat "$report" >> "$GITHUB_STEP_SUMMARY"

if [[ "${POST_REPORT:-}" == true ]]; then
  issue=$(gh api "repos/$repo/issues?state=open&labels=repo-hygiene&per_page=1" --jq '.[0].number // empty')
  if [[ -z "$issue" ]]; then
    issue=$(gh api "repos/$repo/issues" -f title="Repository hygiene report" -f 'labels[]=repo-hygiene' \
      -f body="Weekly branch and issue hygiene report, posted by the Repository hygiene workflow. Policy: docs/contributing/repository-hygiene.md. Keep this issue open and pinned." \
      --jq .number)
  fi
  gh api "repos/$repo/issues/$issue/comments" -F body=@"$report" --jq .html_url
else
  cat "$report"
fi
