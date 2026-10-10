#!/usr/bin/env bash
# Elsa 4 simplification metrics (ADR 0080 D7). Prints a Markdown section to stdout.
# Every number is a cheap, deterministic count over the working tree; trends matter, not exact values.
#
# Usage: quality-metrics.sh [repo-root]
# Called by tools/repo-hygiene/hygiene.sh for the weekly report; safe to run locally.

set -euo pipefail
root="${1:-$(git rev-parse --show-toplevel)}"
cd "$root"

cs_files() { find "$@" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/Migrations/*' 2>/dev/null; }
# grep exits 1 when nothing matches; zero is a valid (and the target) count, so only exit codes above 1 fail.
grep_ok() { grep "$@" || [[ $? -eq 1 ]]; }
# /dev/null keeps grep and cat off stdin when a file list is empty.
count() { grep_ok -hEc "$1" /dev/null "${@:2}" | awk '{s+=$1} END {print s+0}'; }

mapfile -t essentials < <(cs_files src/essentials)
# Production and test files are disjoint, so a file under src/**/tests is counted once, as a test.
mapfile -t src_all < <(cs_files src -not -path '*/tests/*')
mapfile -t tests_all < <(cs_files tests src \( -path 'tests/*' -o -path '*/tests/*' \))
# Test classes can sit in a Migrations folder (EF migration tests); count their methods, not generated migration LOC.
mapfile -t test_classes < <(find tests src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' \( -path 'tests/*' -o -path '*/tests/*' \))

type_decl='^\s*((public|internal|private|protected|file)\s+)?((sealed|static|abstract|partial|readonly|unsafe|ref)\s+)*(class|record|struct|interface|enum|delegate)\s'
public_decl='^\s*public\s+((sealed|static|abstract|partial|readonly|unsafe|ref)\s+)*(class|record|struct|interface|enum|delegate)\s'

public_types=$(count "$public_decl" "${essentials[@]}")
all_types=$(count "$type_decl" "${essentials[@]}")
pct=$(( all_types > 0 ? public_types * 100 / all_types : 0 ))

prod_projects=$(find src -name '*.csproj' -not -path '*/tests/*' -not -name '*Tests.csproj' | wc -l)
test_loc=$(cat /dev/null "${tests_all[@]}" | wc -l)
test_methods=$(count '^\s*\[[A-Za-z]*(Fact|Theory)[](]' "${test_classes[@]}")
obsolete=$(count '\[Obsolete' "${src_all[@]}")
citations=$(grep_ok -hEo 'spec [0-9]{3}|FR-[A-Z]?-?[0-9]{3}|ADR [0-9]{4}' /dev/null "${src_all[@]}" "${tests_all[@]}" | wc -l)
glossary=$(cat docs/glossary/*.md | grep_ok -E '^\| ' | grep_ok -vE '^\| (Term|---)' | wc -l)
constitution=$(cat .specify/memory/constitution*.md | wc -l)
agents=$(wc -l < AGENTS.md)
# Committed blob sizes at HEAD, so neither build output nor local edits or deletions skew the share.
kb() {
  if git rev-parse --verify -q HEAD >/dev/null; then
    git ls-tree -r -l HEAD -- "$@" | awk '{s+=$4} END {print int(s/1024)}'
  else
    du -sk --exclude=bin --exclude=obj "$@" 2>/dev/null | awk '{s+=$1} END {print s+0}'
  fi
}
meta_kb=$(kb docs specs); code_kb=$(kb src tests)
meta_pct=$(( meta_kb * 100 / (meta_kb + code_kb) ))

budget_file=$(find tests -name 'ef-command-budgets.json' -not -path '*/bin/*' 2>/dev/null | head -1)
if [[ -n "$budget_file" ]] && command -v jq >/dev/null; then
  ef=$(jq -r 'to_entries | map("\(.key): \(.value)") | join("; ")' "$budget_file" 2>/dev/null || echo "unreadable")
else
  ef="n/a until #2569"
fi

cat <<MD
### Simplification metrics (ADR 0080, #2559)

| Metric | Value | Target |
|---|---|---|
| Public types in \`src/essentials\` (excluding migrations) | $public_types of $all_types ($pct%) | < 1,500 |
| Production projects | $prod_projects | ~100 |
| Test LOC / test methods | $test_loc / $test_methods | trend down |
| \`[Obsolete]\` members in \`src\` | $obsolete | 0 |
| spec/FR/ADR citations in code | $citations | 0 |
| Glossary terms | $glossary | ~40 user-facing |
| Constitution lines / AGENTS.md lines | $constitution / $agents | < 400 / < 120 |
| Meta share of \`docs\`+\`specs\`+\`src\`+\`tests\` bytes | $meta_pct% | trend down |
| EF command budgets | $ef | budgets only go down |
MD
