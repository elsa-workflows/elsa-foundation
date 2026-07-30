#!/usr/bin/env bash
set -euo pipefail

readonly DRIVER_PROTOCOL_VERSION=1
readonly DISCOVERY_RULES_VERSION=1
readonly INPUT_RULES_VERSION=1

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/../.." && pwd -P)"
driver_path="tools/architecture/restore-zero-ef-certification.sh"

usage() {
  cat <<'EOF'
Usage: restore-zero-ef-certification.sh --force-evaluate --receipt PATH

       restore-zero-ef-certification.sh --self-check

Discovers every repository project independently of solution membership, restores each
project with --force-evaluate, and writes a fail-closed zero-EF certification receipt.
The self-check validates NDJSON receipt assembly without restoring projects.
EOF
}

die() {
  printf 'restore-zero-ef-certification: %s\n' "$1" >&2
  exit 1
}

sha256_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print tolower($1)}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{print tolower($1)}'
  else
    die 'sha256sum or shasum is required.'
  fi
}

sha256_text() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | awk '{print tolower($1)}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 | awk '{print tolower($1)}'
  else
    die 'sha256sum or shasum is required.'
  fi
}

repo_relative_path() {
  local path="$1"
  case "$path" in
    "$repo_root"/*) path="${path#"$repo_root"/}" ;;
    *) die 'discovery returned a path outside the repository.' ;;
  esac
  printf '%s\n' "${path//\\//}"
}

assert_receipt_path_safe() {
  local path="$1"
  case "$path" in
    *$'\n'*|*$'\r'*|*$'\t'*) die 'receipt paths may not contain control characters.' ;;
  esac
}

resolve_assets_project_path() {
  local assets_path="$1"
  local restore_project_path="$2"
  local candidate

  if [[ "$restore_project_path" = /* ]]; then
    candidate="$restore_project_path"
  else
    candidate="$(dirname "$assets_path")/$restore_project_path"
  fi

  [ -f "$candidate" ] || return 1
  (cd "$(dirname "$candidate")" && printf '%s/%s\n' "$(pwd -P)" "$(basename "$candidate")")
}

assets_admit_root_nuget_config() {
  local assets_path="$1"
  local config_path candidate
  jq -e '
    .project.restore.configFilePaths
    | type == "array" and length == 1 and
      (.[0] | type == "string" and length > 0)
  ' "$assets_path" >/dev/null 2>&1 || return 1
  config_path="$(jq -er '.project.restore.configFilePaths[0]' "$assets_path" 2>/dev/null)" || return 1
  [ "$(basename "$config_path")" = 'NuGet.config' ] || return 1
  if [[ "$config_path" = /* ]]; then
    candidate="$config_path"
  else
    candidate="$(dirname "$assets_path")/$config_path"
  fi
  [ -f "$candidate" ] && [ "$candidate" -ef "$repo_root/NuGet.config" ]
}

run_ndjson_shape_self_check() {
  printf '%s\n%s\n' '{"path":"a"}' '{"path":"b"}' > "$tmp_dir/self-check-inputs.ndjson"
  printf '%s\n%s\n' '{"path":"first"}' '{"path":"second"}' > "$tmp_dir/self-check-projects.ndjson"
  jq -n \
    --slurpfile inputs "$tmp_dir/self-check-inputs.ndjson" \
    --slurpfile projects "$tmp_dir/self-check-projects.ndjson" \
    '{inputs:$inputs,projects:$projects,discovery:{projects:($projects | map(.path))}}' \
    > "$tmp_dir/self-check.json"
  jq -e '.inputs == [{path:"a"},{path:"b"}] and .projects == [{path:"first"},{path:"second"}] and .discovery.projects == ["first","second"]' \
    "$tmp_dir/self-check.json" >/dev/null || die 'jq NDJSON receipt assembly self-check failed.'
}

force_evaluate=false
self_check=false
receipt_path=''
while [ "$#" -gt 0 ]; do
  case "$1" in
    --force-evaluate) force_evaluate=true ;;
    --self-check) self_check=true ;;
    --receipt)
      [ "$#" -ge 2 ] || die '--receipt requires a path.'
      receipt_path="$2"
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *) die "unknown argument '$1'." ;;
  esac
  shift
done

if [ "$self_check" = true ]; then
  command -v jq >/dev/null 2>&1 || die 'jq is required.'
  tmp_dir="$(mktemp -d)"
  trap 'rm -rf "$tmp_dir"' EXIT
  run_ndjson_shape_self_check
  printf 'NDJSON receipt assembly self-check passed.\n'
  exit 0
fi

[ "$force_evaluate" = true ] || die '--force-evaluate is required.'
[ -n "$receipt_path" ] || die '--receipt is required.'
[ -f "$repo_root/NuGet.config" ] || die 'repository NuGet.config is required.'
command -v dotnet >/dev/null 2>&1 || die 'dotnet is required.'
command -v git >/dev/null 2>&1 || die 'git is required.'
command -v jq >/dev/null 2>&1 || die 'jq is required.'

git_head="$(git -C "$repo_root" rev-parse --verify HEAD 2>/dev/null)" || die 'a verifiable git HEAD is required.'
[ -n "$git_head" ] || die 'a verifiable git HEAD is required.'
dotnet_sdk_version="$(dotnet --version 2>/dev/null)" || die 'could not determine the dotnet SDK version.'

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
run_ndjson_shape_self_check

symlink_path="$(find "$repo_root" \
  -type d \( -name .git -o -name bin -o -name obj \) -prune -o \
  -type l -print -quit)"
[ -z "$symlink_path" ] || die 'repository discovery does not follow symbolic links.'

declare -a project_paths=()
while IFS= read -r -d '' project; do
  relative="$(repo_relative_path "$project")"
  assert_receipt_path_safe "$relative"
  project_paths+=("$relative")
done < <(find "$repo_root" \
  -type d \( -name .git -o -name bin -o -name obj \) -prune -o \
  -type f -name '*.csproj' -print0)

[ "${#project_paths[@]}" -gt 0 ] || die 'no repository projects were discovered.'
IFS=$'\n' project_paths=($(printf '%s\n' "${project_paths[@]}" | LC_ALL=C sort -u))
unset IFS

printf '%s\n' "${project_paths[@]}" > "$tmp_dir/initial-projects.txt"

project_count="${#project_paths[@]}"
project_index=0
while IFS= read -r relative; do
  project_index=$((project_index + 1))
  printf 'Restoring project %d/%d: %s\n' "$project_index" "$project_count" "$relative"
  project="$repo_root/$relative"
  restore_log="$tmp_dir/restore.log"
  if ! dotnet restore "$project" --force-evaluate --configfile "$repo_root/NuGet.config" >"$restore_log" 2>&1; then
    die "restore failed for '$relative'."
  fi

done < "$tmp_dir/initial-projects.txt"

declare -a project_paths=()
while IFS= read -r -d '' project; do
  relative="$(repo_relative_path "$project")"
  assert_receipt_path_safe "$relative"
  project_paths+=("$relative")
done < <(find "$repo_root" \
  -type d \( -name .git -o -name bin -o -name obj \) -prune -o \
  -type f -name '*.csproj' -print0)
IFS=$'\n' project_paths=($(printf '%s\n' "${project_paths[@]}" | LC_ALL=C sort -u))
unset IFS
printf '%s\n' "${project_paths[@]}" > "$tmp_dir/projects.txt"
cmp -s "$tmp_dir/initial-projects.txt" "$tmp_dir/projects.txt" ||
  die 'repository project discovery changed during restore.'
project_set_sha256="$(while IFS= read -r path; do printf '%s\n' "$path"; done < "$tmp_dir/projects.txt" | sha256_text)"

declare -a input_paths=()
while IFS= read -r -d '' input; do
  relative="$(repo_relative_path "$input")"
  assert_receipt_path_safe "$relative"
  name="$(basename "$relative")"
  case "$relative" in
    *.csproj|*.props|*.targets|*.rsp) input_paths+=("$relative") ;;
    *)
      if [ "$name" = 'global.json' ] || [ "$name" = 'packages.lock.json' ] || \
        [ "$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')" = 'nuget.config' ]; then
        input_paths+=("$relative")
      fi
      ;;
  esac
done < <(find "$repo_root" \
  -type d \( -name .git -o -name bin -o -name obj \) -prune -o \
  -type f \( -name '*.csproj' -o -name '*.props' -o -name '*.targets' -o -name '*.rsp' -o -iname 'nuget.config' -o -name 'global.json' -o -name 'packages.lock.json' \) -print0)
IFS=$'\n' input_paths=($(printf '%s\n' "${input_paths[@]}" | LC_ALL=C sort -u))
unset IFS
[ "${#input_paths[@]}" -gt 0 ] || die 'no dependency-affecting inputs were discovered.'

: > "$tmp_dir/input-lines.txt"
: > "$tmp_dir/input-entries.ndjson"
for relative in "${input_paths[@]}"; do
  hash="$(sha256_file "$repo_root/$relative")"
  printf '%s\t%s\n' "$relative" "$hash" >> "$tmp_dir/input-lines.txt"
  jq -cn --arg path "$relative" --arg sha256 "$hash" '{path:$path,sha256:$sha256}' >> "$tmp_dir/input-entries.ndjson"
done
input_fingerprint_sha256="$(LC_ALL=C sort "$tmp_dir/input-lines.txt" | sha256_text)"

: > "$tmp_dir/project-entries.ndjson"
while IFS= read -r relative; do
  project="$repo_root/$relative"
  assets_path="$(dirname "$project")/obj/project.assets.json"
  [ -f "$assets_path" ] || die "restore did not produce project.assets.json for '$relative'."
  restore_project_path="$(jq -er '.project.restore.projectPath // empty' "$assets_path" 2>/dev/null)" ||
    die "project.assets.json does not identify its restored project for '$relative'."
  resolved_restore_project="$(resolve_assets_project_path "$assets_path" "$restore_project_path")" ||
    die "project.assets.json does not bind to discovered project '$relative'."
  expected_project="$(cd "$(dirname "$project")" && printf '%s/%s\n' "$(pwd -P)" "$(basename "$project")")"
  [ "$resolved_restore_project" = "$expected_project" ] ||
    die "project.assets.json does not bind to discovered project '$relative'."
  assets_admit_root_nuget_config "$assets_path" ||
    die "project.assets.json does not admit the repository NuGet.config for '$relative'."

  assets_relative="$(repo_relative_path "$assets_path")"
  assets_hash="$(sha256_file "$assets_path")"
  jq -cn \
    --arg path "$relative" \
    --arg inputFingerprintSha256 "$input_fingerprint_sha256" \
    --arg assetsPath "$assets_relative" \
    --arg assetsSha256 "$assets_hash" \
    --arg restoreProjectPath "$relative" \
    '{path:$path,inputFingerprintSha256:$inputFingerprintSha256,assets:{path:$assetsPath,sha256:$assetsSha256,restoreProjectPath:$restoreProjectPath}}' \
    >> "$tmp_dir/project-entries.ndjson"
done < "$tmp_dir/projects.txt"

worktree_status_sha256="$(git -C "$repo_root" status --porcelain=v1 -z --untracked-files=all | sha256_text)"
driver_sha256="$(sha256_file "$repo_root/$driver_path")"

assert_receipt_path_safe "$receipt_path"
if [[ "$receipt_path" != /* ]]; then
  receipt_path="$repo_root/$receipt_path"
fi
receipt_parent="$(dirname "$receipt_path")"
mkdir -p "$receipt_parent"
receipt_parent="$(cd "$receipt_parent" && pwd -P)"
receipt_path="$receipt_parent/$(basename "$receipt_path")"
case "$receipt_path" in
  "$repo_root"/*) ;;
  *) die 'receipt path must remain within the repository.' ;;
esac
receipt_temp="$(mktemp "$receipt_parent/.zero-ef-restore-receipt.XXXXXX")"

jq -n \
  --argjson driverProtocolVersion "$DRIVER_PROTOCOL_VERSION" \
  --argjson discoveryRulesVersion "$DISCOVERY_RULES_VERSION" \
  --argjson inputRulesVersion "$INPUT_RULES_VERSION" \
  --arg generatedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg gitHead "$git_head" \
  --arg worktreeStatusSha256 "$worktree_status_sha256" \
  --arg dotnetSdkVersion "$dotnet_sdk_version" \
  --arg driverPath "$driver_path" \
  --arg driverSha256 "$driver_sha256" \
  --arg projectSetSha256 "$project_set_sha256" \
  --arg inputFingerprintSha256 "$input_fingerprint_sha256" \
  --slurpfile inputs "$tmp_dir/input-entries.ndjson" \
  --slurpfile projects "$tmp_dir/project-entries.ndjson" \
  '{
    schemaVersion: 1,
    kind: "elsa-zero-ef-all-project-restore",
    generatedAtUtc: $generatedAtUtc,
    repository: {gitHead: $gitHead, worktreeStatusSha256: $worktreeStatusSha256},
    restore: {
      driverProtocolVersion: $driverProtocolVersion,
      commandTemplate: ["dotnet", "restore", "<project>", "--force-evaluate", "--configfile", "NuGet.config"],
      dotnetSdkVersion: $dotnetSdkVersion,
      driverPath: $driverPath,
      driverSha256: $driverSha256
    },
    discovery: {
      rulesVersion: $discoveryRulesVersion,
      excludedDirectoryNames: [".git", "bin", "obj"],
      projects: ($projects | map(.path)),
      projectSetSha256: $projectSetSha256
    },
    inputs: {rulesVersion: $inputRulesVersion, entries: $inputs, fingerprintSha256: $inputFingerprintSha256},
    projects: $projects
  }' > "$receipt_temp"

mv -f "$receipt_temp" "$receipt_path"
printf 'Wrote zero-EF all-project restore receipt: %s\n' "$receipt_path"
