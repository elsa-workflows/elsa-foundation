# Sourced by the shipped compatibility launchers after setting their historical defaults.
compatibility_usage() {
    printf 'Usage: %s [--pair PRIOR_SHA PRIOR_MARKERS CANDIDATE_SHA CANDIDATE_MARKERS] [receipt-directory]\n' "$0"
    printf 'Use full commit SHAs and quote each marker pair, for example %q.\n' "$prior_markers"
}

compatibility_format() {
    local marker_pattern="^1=([0-9]+)/([0-9]+)/([0-9]+) 2=$definition_engine/[0-9]+/0$"
    if [[ ! "$1" =~ $marker_pattern ]]; then
        printf 'Invalid %s marker pair: %s\n' "$provider" "$1" >&2
        return 2
    fi
    printf 'schema=%s, message protocol=%s, journal codec=%s' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"
}

compatibility_refusal() {
    local stored=$1 expected=$2 stored_format=$3 expected_format=$4
    if [[ "$stored_format" == "$expected_format" ]]; then
        printf 'Incompatible kernel store definitions revision. Stored %s. This build reads %s.' "${stored#* 2=}" "${expected#* 2=}"
    else
        printf 'Unsupported kernel store versions. Stored %s. This build reads %s.' "$stored_format" "$expected_format"
    fi
}

read_compatibility_pair() {
    if [[ "${1:-}" == --help ]]; then compatibility_usage; exit 0; fi
    if [[ "${1:-}" == --pair ]]; then
        if [[ $# -lt 5 ]]; then compatibility_usage >&2; exit 2; fi
        prior_revision=$2 prior_markers=$3 shipped_revision=$4 candidate_markers=$5
        shift 5
    fi
    if [[ $# -gt 1 || "${1:-}" == --* ]]; then compatibility_usage >&2; exit 2; fi
    receipt_directory="${1:-${TMPDIR:-/tmp}}"
    local revision
    for revision in "$prior_revision" "$shipped_revision"; do
        if [[ ! "$revision" =~ ^[0-9a-f]{40}$ ]] || ! git -C "$repo" -c core.fsmonitor=false cat-file -e "$revision^{commit}"; then
            printf 'A local full commit SHA is required: %s\n' "$revision" >&2
            exit 2
        fi
    done
    if [[ "$prior_revision" == "$shipped_revision" || "$prior_markers" == "$candidate_markers" ]]; then
        printf 'The rehearsal requires distinct revisions and marker pairs.\n' >&2
        exit 2
    fi
    local prior_format candidate_format
    prior_format=$(compatibility_format "$prior_markers")
    candidate_format=$(compatibility_format "$candidate_markers")
    prior_refusal=$(compatibility_refusal "$candidate_markers" "$prior_markers" "$candidate_format" "$prior_format")
    candidate_refusal=$(compatibility_refusal "$prior_markers" "$candidate_markers" "$prior_format" "$candidate_format")
}

record_compatibility_pair() {
    printf 'prior %s %s\nshipped %s %s\n' "$prior_revision" "$prior_markers" "$shipped_revision" "$candidate_markers"
}
