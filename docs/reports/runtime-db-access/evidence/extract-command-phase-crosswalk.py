#!/usr/bin/env python3
"""Reconstruct exact exported-span ancestry for the accepted T02 EF ledger."""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
from pathlib import Path
from typing import Any

EXPECTED_INPUT_SHA256 = "924aa56281cea7a7b72872c0453b7a5aa76c5e07d005aab2a56ea87f7f3d6e1e"
EXPECTED_PAIR_COUNTS = {
    ("Coalesced", "HTTP"): 277,
    ("Coalesced", "REST"): 101,
    ("Immediate", "HTTP"): 635,
    ("Immediate", "REST"): 556,
}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def endpoint_chain(
    endpoint: dict[str, Any] | None,
    trace_ref: str,
    spans_by_trace_and_id: dict[tuple[str, str], list[dict[str, Any]]],
) -> dict[str, Any]:
    """Follow only exact same-trace IDs; stop explicitly at absent or ambiguous links."""
    if endpoint is None:
        return {"parentChain": [], "stopReason": "endpoint_missing"}
    if endpoint.get("traceRef") != trace_ref:
        return {"parentChain": [], "stopReason": "endpoint_trace_mismatch"}
    span_id = endpoint.get("spanId")
    if not span_id:
        return {"parentChain": [], "stopReason": "endpoint_span_id_missing"}

    chain: list[dict[str, Any]] = []
    visited: set[str] = set()
    candidates = spans_by_trace_and_id.get((trace_ref, span_id), [])
    if len(candidates) > 1:
        return {
            "parentChain": [{"kind": "endpoint", "spanId": span_id, "parentSpanId": endpoint.get("parentSpanId"), "closureKind": endpoint.get("closureKind")}],
            "stopReason": "ambiguous_endpoint_span",
        }
    if candidates:
        first = candidates[0]
        if first.get("parentSpanId") != endpoint.get("parentSpanId"):
            return {
                "parentChain": [{"kind": "endpoint", "spanId": span_id, "parentSpanId": endpoint.get("parentSpanId"), "closureKind": endpoint.get("closureKind")}],
                "stopReason": "endpoint_exported_parent_mismatch",
            }
        current = first
    else:
        chain.append({
            "kind": "unexported_endpoint_context",
            "spanId": span_id,
            "parentSpanId": endpoint.get("parentSpanId"),
            "closureKind": endpoint.get("closureKind"),
        })
        if span_id in visited:
            return {"parentChain": chain, "stopReason": "cycle_detected"}
        visited.add(span_id)
        parent_id = endpoint.get("parentSpanId")
        if parent_id is None:
            return {"parentChain": chain, "stopReason": "endpoint_has_no_parent"}
        current_candidates = spans_by_trace_and_id.get((trace_ref, parent_id), [])
        if len(current_candidates) > 1:
            chain.append({"kind": "ambiguous_parent", "spanId": parent_id, "candidateCount": len(current_candidates)})
            return {"parentChain": chain, "stopReason": "ambiguous_parent_span"}
        if not current_candidates:
            chain.append({"kind": "unexported_parent", "spanId": parent_id})
            return {"parentChain": chain, "stopReason": "parent_not_exported"}
        current = current_candidates[0]

    while True:
        sid = current.get("spanId")
        if not sid:
            chain.append({"kind": "malformed_exported_span", "spanRef": current.get("spanRef")})
            return {"parentChain": chain, "stopReason": "exported_span_id_missing"}
        if sid in visited:
            chain.append({"kind": "cycle", "spanId": sid, "spanRef": current.get("spanRef")})
            return {"parentChain": chain, "stopReason": "cycle_detected"}
        visited.add(sid)
        chain.append({
            "kind": "exported_span",
            "spanRef": current.get("spanRef"),
            "source": current.get("source"),
            "spanId": sid,
            "parentSpanId": current.get("parentSpanId"),
            "name": current.get("name"),
            "attributes": current.get("attributes", {}),
        })
        parent_id = current.get("parentSpanId")
        if parent_id is None:
            return {"parentChain": chain, "stopReason": "exported_root"}
        parent_candidates = spans_by_trace_and_id.get((trace_ref, parent_id), [])
        if len(parent_candidates) > 1:
            chain.append({"kind": "ambiguous_parent", "spanId": parent_id, "candidateCount": len(parent_candidates)})
            return {"parentChain": chain, "stopReason": "ambiguous_parent_span"}
        if not parent_candidates:
            chain.append({"kind": "unexported_parent", "spanId": parent_id})
            return {"parentChain": chain, "stopReason": "parent_not_exported"}
        current = parent_candidates[0]


def nearest_ancestry(chain_result: dict[str, Any]) -> dict[str, Any]:
    exported = [n for n in chain_result["parentChain"] if n.get("kind") == "exported_span"]
    phase = next((n for n in exported if isinstance(n.get("name"), str) and n["name"].startswith("elsa.runtime.")), None)
    checkpoint = next((n for n in exported if n.get("name") == "elsa.runtime.checkpoint.commit"), None)
    dispatch = next((n for n in exported if n.get("name") == "elsa.runtime.dispatch"), None)
    def selected(node: dict[str, Any] | None, keys: list[str]) -> dict[str, Any] | None:
        if node is None:
            return None
        attrs = node.get("attributes", {})
        return {
            "spanRef": node.get("spanRef"),
            "spanId": node.get("spanId"),
            "name": node.get("name"),
            "attributes": {key: attrs[key] for key in keys if key in attrs},
        }
    return {
        "nearestExportedRuntimePhase": selected(phase, ["elsa.command.kind", "elsa.handler.name", "elsa.work_item.id", "elsa.workflow.execution_id", "elsa.drain.items_processed", "elsa.drain.outermost", "elsa.drain.stop_reason"]),
        "nearestCheckpoint": selected(checkpoint, ["elsa.checkpoint.id", "elsa.checkpoint.persistence_mode", "elsa.checkpoint.mandatory", "elsa.checkpoint.post_commit_intents", "elsa.workflow.execution_id"]),
        "nearestDispatch": selected(dispatch, ["elsa.work_item.id", "elsa.handler.name", "elsa.command.kind", "elsa.outcome", "elsa.workflow.execution_id"]),
    }


def ancestry_signature(result: dict[str, Any]) -> tuple[Any, ...]:
    return tuple((n.get("kind"), n.get("spanId"), n.get("parentSpanId"), n.get("spanRef"), n.get("name")) for n in result.get("parentChain", [])), result.get("stopReason")


def compact_parent_chain(result: dict[str, Any]) -> list[dict[str, Any]]:
    """Keep exact parent links; attributes remain addressable by spanRef in the pinned input."""
    keys = ("kind", "spanRef", "source", "spanId", "parentSpanId", "name", "closureKind", "candidateCount")
    return [{key: node[key] for key in keys if key in node} for node in result.get("parentChain", [])]


def run_self_checks() -> list[dict[str, Any]]:
    checks = []
    spans = {
        ("trace-a", "checkpoint"): [{"spanRef": "sp-cp", "traceRef": "trace-a", "spanId": "checkpoint", "parentSpanId": "request", "name": "elsa.runtime.checkpoint.commit", "source": "engineSpanIndex", "attributes": {"elsa.checkpoint.id": "cp-1", "elsa.checkpoint.persistence_mode": "Immediate"}}],
        ("trace-a", "request"): [{"spanRef": "sp-req", "traceRef": "trace-a", "spanId": "request", "parentSpanId": None, "name": "http.request", "source": "engineSpanIndex", "attributes": {}}],
    }
    valid = endpoint_chain({"traceRef": "trace-a", "spanId": "command", "parentSpanId": "checkpoint", "closureKind": "test"}, "trace-a", spans)
    valid_nearest = nearest_ancestry(valid)["nearestCheckpoint"]
    checks.append({"name": "valid_exact_parent_chain", "passed": valid["stopReason"] == "exported_root" and valid_nearest is not None and valid_nearest["attributes"].get("elsa.checkpoint.id") == "cp-1"})

    malformed = endpoint_chain({"traceRef": "trace-a", "parentSpanId": "checkpoint"}, "trace-a", spans)
    checks.append({"name": "malformed_endpoint_missing_span_id", "passed": malformed["stopReason"] == "endpoint_span_id_missing" and nearest_ancestry(malformed)["nearestCheckpoint"] is None})

    ambiguous = dict(spans)
    ambiguous[("trace-a", "checkpoint")] = spans[("trace-a", "checkpoint")] * 2
    collision = endpoint_chain({"traceRef": "trace-a", "spanId": "command", "parentSpanId": "checkpoint", "closureKind": "test"}, "trace-a", ambiguous)
    checks.append({"name": "ambiguous_parent_rejected", "passed": collision["stopReason"] == "ambiguous_parent_span" and nearest_ancestry(collision)["nearestCheckpoint"] is None})

    cross_trace = endpoint_chain({"traceRef": "trace-b", "spanId": "command", "parentSpanId": "checkpoint", "closureKind": "test"}, "trace-b", spans)
    checks.append({"name": "cross_trace_parent_not_joined", "passed": cross_trace["stopReason"] == "parent_not_exported" and nearest_ancestry(cross_trace)["nearestCheckpoint"] is None})

    return checks


def aggregate(records: list[dict[str, Any]], key_fn) -> list[dict[str, Any]]:
    groups: dict[str, dict[str, Any]] = {}
    for row in records:
        key_obj = key_fn(row)
        key = json.dumps(key_obj, sort_keys=True, separators=(",", ":"))
        item = groups.setdefault(key, {"dimensions": key_obj, "commandPairCount": 0, "tableTokenMembershipUnion": set()})
        item["commandPairCount"] += 1
        item["tableTokenMembershipUnion"].update(row["commandSummary"].get("tableTokens", []))
    return [
        {"dimensions": value["dimensions"], "commandPairCount": value["commandPairCount"], "tableTokenMembershipUnion": sorted(value["tableTokenMembershipUnion"])}
        for _, value in sorted(groups.items())
    ]


def build_crosswalk(source: dict[str, Any]) -> dict[str, Any]:
    all_records: list[dict[str, Any]] = []
    capture_summaries = []
    marker_checks = []
    input_pair_counts: dict[tuple[str, str], int] = {}

    # Indexes are rebuilt inside this loop: references from separate observer processes never mix.
    for capture in source["captures"]:
        cadence = capture["cadence"]
        registries = capture["registries"]
        spans_by_trace_and_id: dict[tuple[str, str], list[dict[str, Any]]] = collections.defaultdict(list)
        for span in registries["spans"]:
            # observerCommandEndpoint is an endpoint alias with only a SpanId;
            # its parent is carried by the endpoint registry, not that alias row.
            if span.get("source") == "engineSpanIndex":
                spans_by_trace_and_id[(span.get("traceRef"), span.get("spanId"))].append(span)
        endpoints_by_ref = {endpoint["endpointRef"]: endpoint for endpoint in registries["endpoints"]}
        provider_types = {item["providerTypeRef"]: item["name"] for item in registries["providerTypes"]}
        trace_ref_to_id = {item["traceRef"]: item["traceId"] for item in registries["traceRefs"]}
        trace_marker_summary = []

        for trace_index, trace in enumerate(capture["requestTraces"]):
            trace_ref = trace["traceRef"]
            trace_id = trace["traceId"]
            request_kind = trace["kind"]
            key = (cadence, request_kind)
            input_pair_counts[key] = input_pair_counts.get(key, 0) + len(trace["commandPairs"])
            if trace_ref_to_id.get(trace_ref) != trace_id:
                raise ValueError(f"traceRef/traceId mismatch in {cadence}/{request_kind}")

            trace_start_count = 0
            trace_terminal_explicit_count = 0
            trace_agreement_count = 0
            trace_markers = []
            for pair_index, pair in enumerate(trace["commandPairs"]):
                start_ref = pair.get("startEndpointRef")
                terminal_explicit = "terminalEndpointRef" in pair
                terminal_ref = pair.get("terminalEndpointRef", start_ref)
                if terminal_explicit:
                    trace_terminal_explicit_count += 1
                start_ep = endpoints_by_ref.get(start_ref)
                terminal_ep = endpoints_by_ref.get(terminal_ref)
                start = endpoint_chain(start_ep, trace_ref, spans_by_trace_and_id)
                terminal = endpoint_chain(terminal_ep, trace_ref, spans_by_trace_and_id)
                start_nearest = nearest_ancestry(start)
                terminal_nearest = nearest_ancestry(terminal)
                start_sig = ancestry_signature(start)
                terminal_sig = ancestry_signature(terminal)
                agreement = start_sig == terminal_sig
                if agreement:
                    trace_agreement_count += 1
                trace_start_count += 1

                connection_type = provider_types.get(pair.get("providerConnectionTypeRef"), "<unresolved-provider-connection-type>")
                command_type = provider_types.get(pair.get("providerCommandTypeRef"), "<unresolved-provider-command-type>")
                source_identity = {
                    "captureCadence": cadence,
                    "requestKind": request_kind,
                    "traceRef": trace_ref,
                    "traceId": trace_id,
                    "commandOrdinal": pair.get("commandOrdinal"),
                    "executeMethod": pair.get("executeMethod"),
                    "occurrence": pair.get("occurrence"),
                    "startEndpointRef": start_ref,
                    "terminalEndpointRef": terminal_ref,
                }
                row = {
                    "sourcePairIdentity": source_identity,
                    "sourceRecordLocator": {
                        "captureCadence": cadence,
                        "requestTraceIndex": trace_index,
                        "commandPairIndex": pair_index,
                        "traceRef": trace_ref,
                        "commandOrdinal": pair.get("commandOrdinal"),
                    },
                    "commandSummary": {
                        key: pair.get(key)
                        for key in ("commandSource", "outcome", "leadingVerb", "tableTokens", "responseTimestampRelation", "contextRef", "connectionRef")
                    },
                    "provider": {
                        "connectionTypeRef": pair.get("providerConnectionTypeRef"),
                        "connectionType": connection_type,
                        "commandTypeRef": pair.get("providerCommandTypeRef"),
                        "commandType": command_type,
                    },
                    "startAncestry": {
                        "endpointRef": start_ref,
                        "endpoint": {key: start_ep.get(key) for key in ("traceRef", "spanId", "parentSpanId", "closureKind")} if start_ep else None,
                        "parentChain": compact_parent_chain(start),
                        "stopReason": start["stopReason"],
                        **start_nearest,
                    },
                    "terminalAncestry": {
                        "endpointRef": terminal_ref,
                        "endpoint": {key: terminal_ep.get(key) for key in ("traceRef", "spanId", "parentSpanId", "closureKind")} if terminal_ep else None,
                        "reusesStartParentChain": terminal_ref == start_ref,
                        **({"parentChain": compact_parent_chain(terminal), **terminal_nearest} if terminal_ref != start_ref else {"nearestExportedRuntimePhase": terminal_nearest["nearestExportedRuntimePhase"], "nearestCheckpoint": terminal_nearest["nearestCheckpoint"], "nearestDispatch": terminal_nearest["nearestDispatch"]}),
                        "stopReason": terminal["stopReason"],
                    },
                    "checks": {
                        "terminalEndpointDefaultApplied": not terminal_explicit,
                        "startAndTerminalUseSameEndpoint": start_ref == terminal_ref,
                        "startAndTerminalParentChainsAgree": agreement,
                    },
                }
                all_records.append(row)

                is_marker = "elsa_runtime_checkpoint_commit" in pair.get("tableTokens", []) and pair.get("leadingVerb") == "INSERT"
                if is_marker:
                    tx = pair.get("transaction") or {}
                    cp = start_nearest["nearestCheckpoint"]
                    command_conn = pair.get("connectionRef")
                    command_context = pair.get("contextRef")
                    tx_conn = command_conn if tx.get("connectionRef") == "sameAsCommand" else tx.get("connectionRef")
                    tx_context = command_context if tx.get("contextRef") == "sameAsCommand" else tx.get("contextRef")
                    starts = [
                        event for event in trace.get("transactionEventRows", [])
                        if event.get("eventSuffix") == "TransactionStarted"
                        and event.get("transactionRef") == tx.get("transactionRef")
                        and event.get("providerRef") == tx.get("providerRef")
                        and event.get("generationRef") == tx.get("generationRef")
                        and event.get("connectionRef") == tx_conn
                        and event.get("contextRef") == tx_context
                    ]
                    same_checkpoint_span = len(starts) == 1 and cp is not None and starts[0].get("spanRef") == cp["spanRef"]
                    started_type = provider_types.get(starts[0].get("providerTransactionTypeRef")) if len(starts) == 1 else None
                    command_is_npgsql = connection_type == "Npgsql.NpgsqlConnection"
                    marker = {
                        "captureCadence": cadence,
                        "requestKind": request_kind,
                        "traceId": trace_id,
                        "commandOrdinal": pair.get("commandOrdinal"),
                        "tableTokens": pair.get("tableTokens", []),
                        "joinState": tx.get("joinState"),
                        "candidateCount": tx.get("candidateCount"),
                        "scopeComparison": tx.get("scopeComparison"),
                        "terminalReferenceMatchesStart": tx.get("terminalReferenceMatchesStart"),
                        "transactionRef": tx.get("transactionRef"),
                        "providerRef": tx.get("providerRef"),
                        "generationRef": tx.get("generationRef"),
                        "effectiveConnectionRef": tx_conn,
                        "effectiveContextRef": tx_context,
                        "matchedTransactionStartedEventCount": len(starts),
                        "matchedTransactionStartedSpanRef": starts[0].get("spanRef") if len(starts) == 1 else None,
                        "matchedTransactionStartedProviderType": started_type,
                        "nearestCheckpointId": (cp or {}).get("attributes", {}).get("elsa.checkpoint.id"),
                        "nearestCheckpointMode": (cp or {}).get("attributes", {}).get("elsa.checkpoint.persistence_mode"),
                        "transactionStartedSpanIsNearestCheckpoint": same_checkpoint_span,
                        "commandConnectionIsNpgsql": command_is_npgsql,
                        "startedTransactionIsNpgsql": started_type == "Npgsql.NpgsqlTransaction",
                        "exactTupleCrosscheckPassed": (
                            tx.get("joinState") == "exactProviderReferenceAndScope"
                            and tx.get("candidateCount") == 1
                            and tx.get("scopeComparison") == "matched"
                            and tx.get("terminalReferenceMatchesStart") is True
                            and len(starts) == 1
                            and same_checkpoint_span
                            and command_is_npgsql
                            and started_type == "Npgsql.NpgsqlTransaction"
                        ),
                    }
                    trace_markers.append(marker)
                    marker_checks.append(marker)

            trace_marker_summary.append({
                "requestKind": request_kind,
                "traceId": trace_id,
                "commandPairCount": len(trace["commandPairs"]),
                "startEndpointCount": trace_start_count,
                "terminalEndpointExplicitCount": trace_terminal_explicit_count,
                "startTerminalAncestryAgreementCount": trace_agreement_count,
                "markerCommandCount": len(trace_markers),
                "markerTupleCrosscheckPassCount": sum(1 for item in trace_markers if item["exactTupleCrosscheckPassed"]),
                "markerTupleCrosscheckFailCount": sum(1 for item in trace_markers if not item["exactTupleCrosscheckPassed"]),
            })
        capture_summaries.append({"captureCadence": cadence, "traces": trace_marker_summary})

    if sum(len(c["requestTraces"]) for c in source["captures"]) != 4:
        raise ValueError("expected four isolated request traces")
    if input_pair_counts != EXPECTED_PAIR_COUNTS:
        raise ValueError(f"unexpected source pair counts: {input_pair_counts!r}")
    if len(all_records) != 1569:
        raise ValueError(f"expected 1,569 records, got {len(all_records)}")
    identities = [
        (r["sourcePairIdentity"]["captureCadence"], r["sourcePairIdentity"]["traceId"], r["sourcePairIdentity"]["commandOrdinal"], r["sourcePairIdentity"]["executeMethod"], r["sourcePairIdentity"]["occurrence"])
        for r in all_records
    ]
    if len(set(identities)) != len(identities):
        raise ValueError("duplicate source pair identity")
    if not all(r["checks"]["startAndTerminalParentChainsAgree"] for r in all_records):
        raise ValueError("start/terminal ancestry disagreement found")
    if len(marker_checks) != 44 or not all(item["exactTupleCrosscheckPassed"] for item in marker_checks):
        raise ValueError(f"checkpoint marker tuple cross-check failed: count={len(marker_checks)}, failures={sum(not item['exactTupleCrosscheckPassed'] for item in marker_checks)}")

    def dimensions(row: dict[str, Any]) -> dict[str, Any]:
        phase = row["startAncestry"]["nearestExportedRuntimePhase"]
        checkpoint = row["startAncestry"]["nearestCheckpoint"]
        dispatch = row["startAncestry"]["nearestDispatch"]
        return {
            "captureCadence": row["sourcePairIdentity"]["captureCadence"],
            "requestKind": row["sourcePairIdentity"]["requestKind"],
            "providerConnectionType": row["provider"]["connectionType"],
            "nearestRuntimeSpanName": phase.get("name") if phase else "<no-exported-runtime-phase>",
            "nearestDispatchHandler": (dispatch or {}).get("attributes", {}).get("elsa.handler.name", "<no-exported-dispatch-handler>"),
            "nearestCheckpointId": (checkpoint or {}).get("attributes", {}).get("elsa.checkpoint.id", "<no-exported-checkpoint>"),
            "nearestCheckpointMode": (checkpoint or {}).get("attributes", {}).get("elsa.checkpoint.persistence_mode", "<no-exported-checkpoint>"),
        }

    group_counts = {
        "byRequest": aggregate(all_records, lambda r: {"captureCadence": r["sourcePairIdentity"]["captureCadence"], "requestKind": r["sourcePairIdentity"]["requestKind"]}),
        "byProviderConnectionType": aggregate(all_records, lambda r: {"captureCadence": r["sourcePairIdentity"]["captureCadence"], "requestKind": r["sourcePairIdentity"]["requestKind"], "providerConnectionType": r["provider"]["connectionType"]}),
        "byNearestRuntimeSpan": aggregate(all_records, lambda r: {"captureCadence": r["sourcePairIdentity"]["captureCadence"], "requestKind": r["sourcePairIdentity"]["requestKind"], "providerConnectionType": r["provider"]["connectionType"], "nearestRuntimeSpanName": (r["startAncestry"]["nearestExportedRuntimePhase"] or {}).get("name", "<no-exported-runtime-phase>")}),
        "byNearestDispatchHandler": aggregate(all_records, lambda r: {"captureCadence": r["sourcePairIdentity"]["captureCadence"], "requestKind": r["sourcePairIdentity"]["requestKind"], "providerConnectionType": r["provider"]["connectionType"], "nearestDispatchHandler": (r["startAncestry"]["nearestDispatch"] or {}).get("attributes", {}).get("elsa.handler.name", "<no-exported-dispatch-handler>")}),
        "byNearestCheckpoint": aggregate(all_records, lambda r: {"captureCadence": r["sourcePairIdentity"]["captureCadence"], "requestKind": r["sourcePairIdentity"]["requestKind"], "providerConnectionType": r["provider"]["connectionType"], "nearestCheckpointId": (r["startAncestry"]["nearestCheckpoint"] or {}).get("attributes", {}).get("elsa.checkpoint.id", "<no-exported-checkpoint>"), "nearestCheckpointMode": (r["startAncestry"]["nearestCheckpoint"] or {}).get("attributes", {}).get("elsa.checkpoint.persistence_mode", "<no-exported-checkpoint>")}),
        "compositeRequestProviderPhaseHandlerCheckpoint": aggregate(all_records, dimensions),
    }
    summary = {
        "commandPairCount": len(all_records),
        "uniqueSourcePairIdentityCount": len(set(identities)),
        "startTerminalAncestryAgreementCount": sum(r["checks"]["startAndTerminalParentChainsAgree"] for r in all_records),
        "terminalEndpointExplicitCount": sum(not r["checks"]["terminalEndpointDefaultApplied"] for r in all_records),
        "terminalEndpointDefaultedToStartCount": sum(r["checks"]["terminalEndpointDefaultApplied"] for r in all_records),
        "parentChainStopReasons": dict(sorted(collections.Counter(r["startAncestry"]["stopReason"] for r in all_records).items())),
        "endpointClosureKindCounts": dict(sorted(collections.Counter(r["startAncestry"].get("endpoint", {}).get("closureKind", "<missing-endpoint>") for r in all_records).items())),
        "nearestRuntimeSpanNameCounts": dict(sorted(collections.Counter((r["startAncestry"]["nearestExportedRuntimePhase"] or {}).get("name", "<no-exported-runtime-phase>") for r in all_records).items())),
        "checkpointMarkerInsertCommandCount": len(marker_checks),
        "checkpointMarkerExactTupleCrosscheckPassCount": sum(item["exactTupleCrosscheckPassed"] for item in marker_checks),
    }
    return {
        "schema": "t02-command-phase-crosswalk/v1",
        "source": {
            "relativePath": "docs/reports/runtime-db-access/evidence/ef-identity-accounting-2026-10-06.json",
            "sha256": EXPECTED_INPUT_SHA256,
            "captureCadences": [capture["cadence"] for capture in source["captures"]],
            "referenceIsolation": "Span, endpoint, trace, provider and context references were resolved only within their containing capture and request trace.",
        },
        "method": {
            "parentChain": "Begin at each command pair startEndpointRef; use its exact traceRef/spanId/parentSpanId, then follow only unique same-trace engineSpanIndex spanId-to-parentSpanId entries in that capture. observerCommandEndpoint registry rows are endpoint aliases with no parent and are excluded from ancestor lookup. Missing/ambiguous/cyclic links stop explicitly.",
            "spanMetadata": "The crosswalk stores exact ordered spanRef/spanId/parentSpanId/name links. Other source span attributes are reconstructed by spanRef from the SHA-pinned input ledger; only selected checkpoint/dispatch/phase attributes are projected inline.",
            "phase": "Nearest exported runtime span in the endpoint context chain whose name begins elsa.runtime.; it may be the current endpoint context or an ancestor. This is an exported span name, not a repository method, exclusive owner, or awaited-work claim.",
            "terminalEndpoint": "The source normalization contract says an omitted terminalEndpointRef equals startEndpointRef; the crosswalk records that default separately. All source command pairs omit the distinct terminal ref.",
            "tableTokens": "Token unions are overlapping memberships; a pair may touch multiple tokens. Group counts count command pairs, not statements or round trips.",
        },
        "summary": summary,
        "captureSummaries": capture_summaries,
        "groupCounts": group_counts,
        "checkpointMarkerTupleCrosscheck": marker_checks,
        "commandPairs": all_records,
    }


def write_private(path: Path, data: str) -> None:
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(data)
            stream.flush()
    except BaseException:
        try:
            os.close(fd)
        except OSError:
            pass
        raise


def render_markdown(doc: dict[str, Any], input_hash: str, self_checks: list[dict[str, Any]]) -> str:
    summary = doc["summary"]
    lines = [
        "# T02 command-to-phase span crosswalk candidate",
        "",
        f"Input: `docs/reports/runtime-db-access/evidence/ef-identity-accounting-2026-10-06.json` SHA-256 `{input_hash}`.",
        "",
        "This private derivative reconstructs ancestry from the accepted normalized EF identity ledger. It adds no capture and makes no repository-caller or exclusive-ownership claim.",
        "",
        "## Reconstruction checks",
        "",
        f"- {summary['commandPairCount']} input pairs produced; {summary['uniqueSourcePairIdentityCount']} unique source identities.",
        f"- Start/terminal ancestry agreements: {summary['startTerminalAncestryAgreementCount']}; explicit terminal endpoint references: {summary['terminalEndpointExplicitCount']}; normalized default-to-start references: {summary['terminalEndpointDefaultedToStartCount']}.",
        f"- 44 checkpoint marker INSERT pairs; exact tuple cross-check passes: {summary['checkpointMarkerExactTupleCrosscheckPassCount']}.",
        f"- Endpoint closure kinds: `{json.dumps(summary['endpointClosureKindCounts'], sort_keys=True)}`.",
        f"- Parent-chain stop reasons: `{json.dumps(summary['parentChainStopReasons'], sort_keys=True)}`.",
        "- The input normalization contract defaults omitted terminalEndpointRef to startEndpointRef. Since all 1,569 command pairs omit a distinct terminal ref, the equality check confirms this normalized relationship; it is not an independent second endpoint observation.",
        "- Each chain stops when its next parent is absent from that request's exported engine-span registry. The 216 request-root endpoint contexts carry the documented no-phase closure kind; they are not assigned a runtime phase. The two observerCommandEndpoint span rows per capture are endpoint aliases only and are not treated as exported parent nodes.",
        "",
        "## Counts by nearest exported runtime span",
        "",
        "| Cadence | Request | Provider connection type | Span name or explicit absence | Command pairs | Table-token membership union (overlapping) |",
        "|---|---|---|---|---:|---|",
    ]
    for row in doc["groupCounts"]["byNearestRuntimeSpan"]:
        d = row["dimensions"]
        tokens = ", ".join(row["tableTokenMembershipUnion"])
        lines.append(f"| {d['captureCadence']} | {d['requestKind']} | `{d['providerConnectionType']}` | `{d['nearestRuntimeSpanName']}` | {row['commandPairCount']} | {tokens} |")
    lines.extend(["", "## Counts by provider, dispatch handler, and checkpoint", ""])
    for label, key in [
        ("Provider connection type", "byProviderConnectionType"),
        ("Nearest dispatch handler", "byNearestDispatchHandler"),
        ("Nearest checkpoint ID/mode", "byNearestCheckpoint"),
    ]:
        lines.extend([f"### {label}", "", "| Dimensions | Command pairs | Table-token membership union (overlapping) |", "|---|---:|---|"])
        for row in doc["groupCounts"][key]:
            lines.append(f"| `{json.dumps(row['dimensions'], sort_keys=True)}` | {row['commandPairCount']} | {', '.join(row['tableTokenMembershipUnion'])} |")
        lines.append("")
    lines.extend([
        "## Bounded extractor controls",
        "",
    ])
    for check in self_checks:
        lines.append(f"- `{check['name']}`: {'PASS' if check['passed'] else 'FAIL'}.")
    lines.extend([
        "",
        "The JSON retains every source pair, capture-local identity, provider type, exact start and terminal parent chain, nearest available runtime/checkpoint/dispatch span, and the 44-marker tuple cross-check. Missing parents remain explicit. Runtime span ancestry is not exact repository-method attribution; request-root commands have no runtime phase, and a child span under a request root is not an ancestor of that root's commands. Handler/table-token aggregation does not establish exclusive ownership, awaited work, row identity/lifecycle, SQL statement count, or network round trips.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--out-dir", required=True, type=Path)
    args = parser.parse_args()
    args.out_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(args.out_dir, 0o700)
    if args.input.is_symlink() or not args.input.is_file():
        raise SystemExit("input must be a regular non-symlink file")
    actual_hash = sha256_file(args.input)
    if actual_hash != EXPECTED_INPUT_SHA256:
        raise SystemExit(f"input SHA mismatch: got {actual_hash}")
    source = json.loads(args.input.read_text(encoding="utf-8"))
    checks = run_self_checks()
    if not all(item["passed"] for item in checks):
        raise SystemExit(f"self-check failure: {checks}")
    crosswalk = build_crosswalk(source)
    crosswalk["source"]["actualInputSha256"] = actual_hash
    crosswalk["boundedSelfChecks"] = checks
    json_text = json.dumps(crosswalk, ensure_ascii=False, indent=2) + "\n"
    markdown = render_markdown(crosswalk, actual_hash, checks)
    write_private(args.out_dir / "command-phase-crosswalk.json", json_text)
    summary = {key: value for key, value in crosswalk.items() if key != "commandPairs"}
    write_private(args.out_dir / "command-phase-crosswalk-summary.json", json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
    write_private(args.out_dir / "command-phase-crosswalk.md", markdown)
    print(json.dumps({
        "inputSha256": actual_hash,
        "jsonBytes": len(json_text.encode("utf-8")),
        "commandPairCount": crosswalk["summary"]["commandPairCount"],
        "markerTuplePasses": crosswalk["summary"]["checkpointMarkerExactTupleCrosscheckPassCount"],
        "stopReasons": crosswalk["summary"]["parentChainStopReasons"],
        "selfChecks": checks,
        "outputDirectory": str(args.out_dir),
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
