#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
docs_maps="$repo_root/docs/maps"
tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
mkdir -p "$docs_maps"

repo_path() {
  local path="$1"
  path="$(cd "$(dirname "$path")" && pwd)/$(basename "$path")"
  printf '%s\n' "${path#$repo_root/}" | sed 's#\\#/#g'
}

escape_cell() {
  local value="${1:-}"
  if [ -z "${value// }" ]; then
    printf '%s' "-"
  else
    printf '%s' "$value" | sed 's/|/\\|/g'
  fi
}

join_br() {
  awk 'BEGIN{first=1}{if(!first)printf "<br>"; printf "%s",$0; first=0}'
}

domain_group() {
  local project="$1"
  case "$project" in
    Server) printf '%s\n' "Elsa.Server" ;;
    Test.*) printf '%s\n' "Test" ;;
    Elsa3.*) printf '%s\n' "Elsa3" ;;
    Elsa.*) printf '%s\n' "$project" | awk -F. '{print $1 "." $2}' ;;
    *) printf '%s\n' "Other" ;;
  esac
}

project_name_from_include() {
  local include="$1"
  include="${include##*\\}"
  include="${include##*/}"
  printf '%s\n' "${include%.csproj}"
}

project_refs() {
  local file="$1"
  { grep -oE '<ProjectReference[^>]+Include="[^"]+"' "$file" 2>/dev/null || true; } \
    | sed -E 's/.*Include="([^"]+)".*/\1/' \
    | while IFS= read -r include; do project_name_from_include "$include"; done \
    | sort -u
}


central_package_version() {
  local id="$1"
  local project_name="$2"
  local central_file="$repo_root/Directory.Packages.props"
  [ -f "$central_file" ] || return 0

  awk -v id="$id" -v project_name="$project_name" '
    function attr(line, name, pattern, value) {
      pattern = name "=\"[^\"]+\""
      if (match(line, pattern)) {
        value = substr(line, RSTART + length(name) + 2, RLENGTH - length(name) - 3)
        return value
      }
      return ""
    }
    function condition_matches(condition, compared_project) {
      if (condition == "") return 1
      if (condition ~ /^\047\$\(MSBuildProjectName\)\047 == \047/) {
        compared_project = condition
        sub(/^\047\$\(MSBuildProjectName\)\047 == \047/, "", compared_project)
        sub(/\047$/, "", compared_project)
        return compared_project == project_name
      }
      if (condition ~ /^\047\$\(MSBuildProjectName\)\047 != \047/) {
        compared_project = condition
        sub(/^\047\$\(MSBuildProjectName\)\047 != \047/, "", compared_project)
        sub(/\047$/, "", compared_project)
        return compared_project != project_name
      }
      return 0
    }
    /<PackageVersion[[:space:]]/ {
      include = attr($0, "Include")
      if (include != id) next
      version = attr($0, "Version")
      condition = attr($0, "Condition")
      if (condition_matches(condition)) selected = version
    }
    END { if (selected != "") print selected }
  ' "$central_file" | tail -n 1
}

package_refs() {
  local file="$1"
  local project_name
  project_name="$(basename "$file" .csproj)"
  { grep -oE '<PackageReference[^>]+' "$file" 2>/dev/null || true; } \
    | while IFS= read -r line; do
      local id version
      id="$(printf '%s\n' "$line" | sed -nE 's/.*Include="([^"]+)".*/\1/p')"
      version="$(printf '%s\n' "$line" | sed -nE 's/.*Version="([^"]+)".*/\1/p')"
      [ -z "$version" ] && version="$(central_package_version "$id" "$project_name")"
      [ -z "$version" ] && version="(unspecified)"
      [ -n "$id" ] && printf '%s %s\n' "$id" "$version"
    done \
    | sort -u
}

owner_project_path() {
  local file="$1" dir project best_project="" best_len=0 len
  while IFS="$(printf '\t')" read -r dir project; do
    case "$file" in
      "$dir"/*)
        len="${#dir}"
        if [ "$len" -gt "$best_len" ]; then
          best_len="$len"
          best_project="$project"
        fi
        ;;
    esac
  done < "$tmp_dir/project-dirs.tsv"
  if [ -n "$best_project" ]; then
    printf '%s\n' "$best_project"
    return 0
  fi
  return 1
}

feature_properties() {
  local file="$1"
  sed -nE 's/^[[:space:]]*public[[:space:]]+(.+)[[:space:]]+([A-Za-z0-9_]+)[[:space:]]*[{][[:space:]]*get;[[:space:]]*set;[[:space:]]*[}].*/\2: \1/p' "$file" \
    | sed 's/[[:space:]]*$//' \
    | sort -u
}

feature_option_signals() {
  local file="$1" text
  text="$(tr '\n' ' ' < "$file")"
  printf '%s' "$text" | grep -Eiq 'Path\.GetTempPath|DefaultConnectionString|TimeSpan\.From|=[[:space:]]*true|=[[:space:]]*false|=[[:space:]]*100|=[[:space:]]*string\.Empty' && printf '%s\n' "code default" || true
  printf '%s' "$text" | grep -Eiq 'FolderPath|Directory|FilePath|LocalCacheDirectory|LocksFolderPath' && printf '%s\n' "filesystem path signal" || true
  printf '%s' "$text" | grep -Eiq 'ConnectionString|Secret|Password|Token|Key' && printf '%s\n' "sensitive or deployment-specific value signal" || true
  printf '%s' "$text" | grep -Eiq 'Type[[:space:]]*\{|TypeName|Type[[:space:]]*=|GetLoadedType' && printf '%s\n' "type-name selection signal" || true
  printf '%s' "$text" | grep -Eiq 'FeatureConfigurationException|requires exactly one|requires a non-empty|must specify|not configured' && printf '%s\n' "validation/requiredness guard in code" || true
}

feature_classes() {
  local file="$1"
  awk '
    function trim(value) {
      gsub(/^[ \t\r\n]+/, "", value)
      gsub(/[ \t\r\n]+$/, "", value)
      return value
    }
    function shell_feature_name(args, value) {
      value = ""
      if (match(args, /name:[[:space:]]*"[^"]+"/)) {
        value = substr(args, RSTART, RLENGTH)
        sub(/^name:[[:space:]]*"/, "", value)
        sub(/"$/, "", value)
      } else {
        args = trim(args)
        if (match(args, /^"[^"]+"/)) {
          value = substr(args, RSTART + 1, RLENGTH - 2)
        }
      }
      return value
    }
    {
      line = $0
      if (index(line, "[ShellFeature(") > 0) {
        attr = substr(line, index(line, "[ShellFeature(") + length("[ShellFeature("))
        collecting_attr = 1
      } else if (collecting_attr) {
        attr = attr " " line
      }
      if (collecting_attr && index(line, ")]") > 0) {
        collecting_attr = 0
      }

      normalized = line
      gsub(/[ \t\r]+/, " ", normalized)
      normalized = trim(normalized)
      if (normalized !~ /^public (abstract )?((sealed|partial) )*class [A-Za-z0-9_]+[[:space:]]*:[[:space:]]*/) {
        next
      }
      class_name = normalized
      sub(/^.*class /, "", class_name)
      sub(/[[:space:]]*:.*$/, "", class_name)
      if (class_name !~ /^[A-Za-z0-9_]+$/) {
        next
      }
      base_text = normalized
      sub(/^.*:[[:space:]]*/, "", base_text)
      sub(/[[:space:]]*\{.*$/, "", base_text)
      base_text = trim(base_text)
      if (base_text !~ /IShellFeature|FastEndpointsFeatureBase|EFCore.*FeatureBase|FeatureBase/) {
        next
      }
      is_abstract = normalized ~ /^public abstract / ? "True" : "False"
      feature_id = shell_feature_name(attr)
      if (feature_id == "") {
        feature_id = "-"
      }
      print class_name "\t" is_abstract "\t" base_text "\t" feature_id
      attr = ""
      collecting_attr = 0
    }
  ' "$file"
}

default_feature_keys() {
  local file="$repo_root/src/Apps/Elsa.Server/appsettings.json"
  [ -f "$file" ] || return 0
  awk '
    /"CShells"[[:space:]]*:/ { in_cshells = 1 }
    in_cshells && /"Shells"[[:space:]]*:/ { in_shells = 1 }
    in_shells && /"default"[[:space:]]*:/ { in_default = 1 }
    in_default && /"Features"[[:space:]]*:/ {
      in_features = 1
      depth = 0
      next
    }
    in_features {
      opens = gsub(/\{/, "{")
      closes = gsub(/\}/, "}")
      if (depth == 0 && match($0, /^[[:space:]]*"[^"]+"[[:space:]]*:/)) {
        key = substr($0, RSTART, RLENGTH)
        sub(/^[[:space:]]*"/, "", key)
        sub(/"[[:space:]]*:$/, "", key)
        print key
      }
      depth += opens - closes
      if (depth < 0) {
        exit
      }
    }
  ' "$file" | sort
}

find "$repo_root/src" -name '*.csproj' -type f 2>/dev/null | sort > "$tmp_dir/project-files.txt"
: > "$tmp_dir/projects.tsv"
: > "$tmp_dir/project-dirs.tsv"

while IFS= read -r project_file; do
  name="$(basename "$project_file" .csproj)"
  rel="$(repo_path "$project_file")"
  domain="$(domain_group "$name")"
  refs="$(project_refs "$project_file" | join_br)"
  packages="$(package_refs "$project_file" | join_br)"
  printf '%s\t%s\t%s\t%s\t%s\n' "$name" "$rel" "$domain" "$refs" "$packages" >> "$tmp_dir/projects.tsv"
  printf '%s\t%s\n' "$(dirname "$project_file")" "$project_file" >> "$tmp_dir/project-dirs.tsv"
done < "$tmp_dir/project-files.txt"

: > "$tmp_dir/features.tsv"
find "$repo_root/src" -name '*.cs' -type f -print0 2>/dev/null \
  | xargs -0 grep -El '^[[:space:]]*public[[:space:]]+(abstract[[:space:]]+)?((sealed|partial)[[:space:]]+)*class[[:space:]]+[A-Za-z0-9_]+[[:space:]]*:[^{\r\n]*(IShellFeature|FastEndpointsFeatureBase|EFCore.*FeatureBase|FeatureBase)' \
  | sort > "$tmp_dir/source-files.txt"

while IFS= read -r file; do
  project_file="$(owner_project_path "$file" || true)"
  [ -n "$project_file" ] || continue
  project="$(basename "$project_file" .csproj)"
  domain="$(domain_group "$project")"
  rel="$(repo_path "$file")"
  props="$(feature_properties "$file" | join_br)"
  signals="$(feature_option_signals "$file" | join_br)"
  while IFS="$(printf '\t')" read -r class_name is_abstract base_text feature_id; do
    [ -n "$class_name" ] || continue
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$domain" "$project" "$class_name" "$feature_id" "$is_abstract" "$base_text" "$rel" "$props" "$signals" >> "$tmp_dir/features.tsv"
  done < <(feature_classes "$file")
done < "$tmp_dir/source-files.txt"

sort -t "$(printf '\t')" -k1,1 -k2,2 -k3,3 "$tmp_dir/features.tsv" -o "$tmp_dir/features.tsv"
awk -F'\t' '{print $2}' "$tmp_dir/features.tsv" | sort -u > "$tmp_dir/feature-projects.txt"
awk -F'\t' '{print $2 "\t" $4 "\t" $3}' "$tmp_dir/features.tsv" | sort -u > "$tmp_dir/feature-project-ids.tsv"
awk -F'\t' '$4 != "-" {count[$4]++; classes[$4]=classes[$4] ? classes[$4] "<br>" $3 : $3; projects[$4]=projects[$4] ? projects[$4] "<br>" $2 : $2} END {for (id in count) if (count[id] > 1) print id "\t" classes[id] "\t" projects[id]}' "$tmp_dir/features.tsv" | sort > "$tmp_dir/duplicates.tsv"

feature_count="$(grep -c . "$tmp_dir/features.tsv" || true)"
missing_concrete_count="$(awk -F'\t' '$5 == "False" && $4 == "-" {count++} END{print count+0}' "$tmp_dir/features.tsv")"
duplicate_count="$(grep -c . "$tmp_dir/duplicates.tsv" || true)"
feature_project_count="$(grep -c . "$tmp_dir/feature-projects.txt" || true)"

{
  echo "# Feature Dependency Map"
  echo
  echo 'Generated by `tools/maps/generate-feature-dependency-map`.'
  echo
  echo 'Records CShells feature identity, public feature properties, and dependency evidence from direct project/package references. It does not generate appsettings JSON and does not treat `src/Apps/Elsa.Server` composition choices as canonical.'
  echo
  echo "## Summary"
  echo
  echo "- Feature classes: $feature_count"
  echo "- Concrete features missing explicit ShellFeature ID: $missing_concrete_count"
  echo "- Duplicate explicit feature IDs: $duplicate_count"
  echo "- Feature-bearing source projects: $feature_project_count"
  echo '- IConfiguration feature-registration shape observed from: `src/Apps/Elsa.Server/appsettings.json`'
  echo
  echo "## IConfiguration Shape Evidence"
  echo
  echo 'The test shell is not a composition source of truth. The only evidence taken from `src/Apps/Elsa.Server/appsettings.json` is the JSON shape: `CShells:Shells:{shellName}:Features:{featureId}` with optional nested feature settings such as `Options`, plus shell-level `Configuration`.'
  echo
  echo "Observed default-shell feature keys:"
  echo
  if default_feature_keys | grep -q .; then
    default_feature_keys | while IFS= read -r feature_id; do echo "- \`$feature_id\`"; done
  else
    echo "- none"
  fi
  echo
  echo "## Duplicate Feature IDs"
  echo
  if [ "$duplicate_count" -eq 0 ]; then
    echo "No duplicate explicit feature IDs were discovered."
  else
    echo "| Feature ID | Feature classes | Projects |"
    echo "|---|---|---|"
    while IFS="$(printf '\t')" read -r feature_id classes projects; do
      echo "| $feature_id | $(escape_cell "$classes") | $(escape_cell "$projects") |"
    done < "$tmp_dir/duplicates.tsv"
  fi
  echo
  echo "## Feature Identity And Configuration Inputs"
  echo
  echo "| Feature ID | Feature class | Abstract | Project | Public feature properties | Option/config signals | File |"
  echo "|---|---|---|---|---|---|---|"
  while IFS= read -r row; do
    domain="$(printf '%s\n' "$row" | awk -F'\t' '{print $1}')"
    project="$(printf '%s\n' "$row" | awk -F'\t' '{print $2}')"
    class_name="$(printf '%s\n' "$row" | awk -F'\t' '{print $3}')"
    feature_id="$(printf '%s\n' "$row" | awk -F'\t' '{print $4}')"
    is_abstract="$(printf '%s\n' "$row" | awk -F'\t' '{print $5}')"
    rel="$(printf '%s\n' "$row" | awk -F'\t' '{print $7}')"
    props="$(printf '%s\n' "$row" | awk -F'\t' '{print $8}')"
    signals="$(printf '%s\n' "$row" | awk -F'\t' '{print $9}')"
    [ -n "$props" ] || props="-"
    [ -n "$signals" ] || signals="-"
    echo "| $feature_id | $class_name | $is_abstract | $project | $(escape_cell "$props") | $(escape_cell "$signals") | [$(basename "$rel")](../../$rel) |"
  done < "$tmp_dir/features.tsv"
  echo
  echo "## Feature Dependency Evidence"
  echo
  echo "Rows below are dependency evidence, not final policy. Feature-project references means the feature's owning project directly references another project that also declares one or more feature classes. Core/helper references are still important runtime prerequisites, but they are not feature activation IDs by themselves."
  echo
  echo "| Feature ID | Project | Feature-project references | Core/helper project references | Direct external packages |"
  echo "|---|---|---|---|---|"
  while IFS= read -r row; do
    project="$(printf '%s\n' "$row" | awk -F'\t' '{print $2}')"
    feature_id="$(printf '%s\n' "$row" | awk -F'\t' '{print $4}')"
    is_abstract="$(printf '%s\n' "$row" | awk -F'\t' '{print $5}')"
    [ "$is_abstract" = "False" ] || continue
    project_row="$(awk -F'\t' -v project="$project" '$1 == project {print; exit}' "$tmp_dir/projects.tsv")"
    [ -n "$project_row" ] || continue
    refs="$(printf '%s\n' "$project_row" | awk -F'\t' '{print $4}')"
    packages="$(printf '%s\n' "$project_row" | awk -F'\t' '{print $5}')"
    : > "$tmp_dir/feature-refs.current"
    : > "$tmp_dir/core-refs.current"
    if [ -n "$refs" ]; then
      printf '%s\n' "$refs" | sed 's/<br>/\
/g' | while IFS= read -r ref; do
        [ -n "$ref" ] || continue
        if grep -Fqx "$ref" "$tmp_dir/feature-projects.txt"; then
          ids="$(awk -F'\t' -v project="$ref" '$1 == project {print ($2 != "-" ? $2 : $3)}' "$tmp_dir/feature-project-ids.tsv" | sort -u | awk 'BEGIN{first=1}{if(!first)printf ", "; printf "%s",$0; first=0}')"
          printf '%s (%s)\n' "$ref" "$ids" >> "$tmp_dir/feature-refs.current"
        else
          printf '%s\n' "$ref" >> "$tmp_dir/core-refs.current"
        fi
      done
    fi
    feature_ref_cell="$(sort -u "$tmp_dir/feature-refs.current" | join_br)"
    core_ref_cell="$(sort -u "$tmp_dir/core-refs.current" | join_br)"
    [ -n "$feature_ref_cell" ] || feature_ref_cell="-"
    [ -n "$core_ref_cell" ] || core_ref_cell="-"
    [ -n "$packages" ] || packages="-"
    echo "| $feature_id | $project | $(escape_cell "$feature_ref_cell") | $(escape_cell "$core_ref_cell") | $(escape_cell "$packages") |"
  done < "$tmp_dir/features.tsv"
  echo
  echo "## Gaps For Generator Work"
  echo
  echo "- Direct project references do not prove that a referenced feature must be activated in the same shell."
  echo "- Public feature properties expose possible IConfiguration keys, but required/default/secret/path/type-name classification still needs an architecture decision."
  echo "- Feature IDs must stay explicit on concrete features; abstract feature bases may remain unannotated unless base metadata becomes useful."
  echo "- Nuplane assembly-loading settings are host/loading evidence, not feature-selection evidence."
  echo "- A CShells appsettings generator should consume this map plus an approved configuration/settings classification before emitting JSON."
} > "$docs_maps/feature-dependency-map.md"

echo "Generated maps:"
echo " - docs/maps/feature-dependency-map.md"
