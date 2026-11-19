#!/usr/bin/env python3
"""Compare actual source-host SQLite catalog publications with the frozen BPMN contract.

Read-only, no regeneration of the oracle. HTTP descriptor comparison and Studio journeys
remain separate witnesses. Build metadata identifies the host build, not the authoring version.
"""
import argparse
import hashlib
import json
import sqlite3
from pathlib import Path


def project(row):
    facets = json.loads(row["DesignFacetsSource"] or "[]")
    outcomes = next((facet["payload"] for facet in facets if facet["kind"] == "elsa.outcomes"), None)
    structure = next((facet for facet in facets if facet["kind"] == "elsa.bpmn.structure"), None)
    inputs = []
    for value in json.loads(row["InputsSource"] or "[]"):
        inputs.append({
            "key": value["referenceKey"], "name": value["name"], "type": value["type"]["alias"],
            "collectionKind": value["type"]["collectionKind"],
            **{key: value.get(key) for key in ("defaultSyntax", "defaultValue", "isNullable", "isRequired", "uiHint", "uiSpecifications")}
        })
    return {
        "activityTypeKey": row["ActivityTypeKey"],
        "alias": json.loads(row["DescriptorPayloadSource"])["typeAlias"],
        "category": row["Category"], "executionType": "Action" if row["ExecutionType"] == 0 else str(row["ExecutionType"]),
        "inputs": sorted(inputs, key=lambda value: value["key"]),
        "outputs": json.loads(row["OutputsSource"] or "[]"),
        "outcomes": {"declared": outcomes is not None, "dynamic": outcomes.get("dynamicOutcomes") if outcomes else None,
                     "ports": sorted(port["name"] for port in outcomes.get("ports", [])) if outcomes else []},
        "structure": ({"kind": structure["kind"], "schemaVersion": structure["schemaVersion"],
                       **{key: structure["payload"].get(key) for key in ("mode", "slots", "supportsScopedVariables")}}
                      if structure else None),
        "version": row["Version"].partition("+")[0]
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--source-revision", required=True)
    parser.add_argument("--tenant-id", default=None, help="Visible tenant; global rows remain visible")
    parser.add_argument("--closed-snapshot", action="store_true", help="Read an immutable, closed backup without live sidecars")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--oracle", type=Path, default=Path(__file__).resolve().parents[2] / "docs/reports/evidence/bpmn-extraction-baseline/activity-contract-fingerprint.json")
    args = parser.parse_args()
    expected = json.loads(args.oracle.read_text())
    before = hashlib.sha256(args.database.read_bytes()).hexdigest() if args.closed_snapshot else None
    if args.closed_snapshot and any(Path(str(args.database) + suffix).exists() for suffix in ("-wal", "-shm", "-journal")):
        raise AssertionError("A closed snapshot must not have live sidecars")
    connection = sqlite3.connect(args.database.resolve().as_uri() + "?mode=ro" + ("&immutable=1" if args.closed_snapshot else ""), uri=True)
    connection.row_factory = sqlite3.Row
    connection.execute("BEGIN")
    actual, identities = [], []
    for item in expected:
        rows = connection.execute("""
            SELECT v.*, d.ActivityTypeKey, d.Category, p.Id AS PublicationId, p.PublishedAt, p.ProviderFingerprint
            FROM elsa_activity_definition_versions_v2 v
            JOIN elsa_activity_definitions d ON d.Id = v.DefinitionId AND d.TenantScopeKey = v.TenantScopeKey
            JOIN elsa_activity_definition_authoring a ON a.DefinitionId = d.Id AND a.TenantScopeKey = d.TenantScopeKey
            JOIN elsa_activity_version_publications p ON p.DefinitionVersionId = v.Id AND p.TenantScopeKey = v.TenantScopeKey
            WHERE d.ActivityTypeKey = ? AND a.RecommendedVersionId = v.Id
              AND (d.TenantId IS NULL OR d.TenantId = ?) AND p.Lifecycle = 0
              AND p.DefinitionId = d.Id AND p.Version = v.Version AND p.ActivityTypeKey = d.ActivityTypeKey
            """, (item["activityTypeKey"], args.tenant_id)).fetchall()
        if len(rows) != 1:
            raise AssertionError(f"Expected one actual recommended publication for {item['activityTypeKey']}, found {len(rows)}")
        row = rows[0]
        actual.append(project(row))
        identities.append({"activityTypeKey": row["ActivityTypeKey"], "versionId": row["Id"], "publishedVersion": row["Version"], "hash": row["Hash"], "publicationId": row["PublicationId"], "publishedAt": row["PublishedAt"], "providerFingerprint": row["ProviderFingerprint"]})
    connection.rollback()
    connection.close()
    if args.closed_snapshot and (hashlib.sha256(args.database.read_bytes()).hexdigest() != before or
                                any(Path(str(args.database) + suffix).exists() for suffix in ("-wal", "-shm", "-journal"))):
        raise AssertionError("The closed publication snapshot changed")
    receipt = {"sourceRevision": args.source_revision, "oracleSha256": hashlib.sha256(args.oracle.read_bytes()).hexdigest(),
               "closedSnapshotSha256": before, "immutableSnapshot": args.closed_snapshot,
               "publishedIdentities": identities, "actualFingerprint": actual, "matchesFrozenFingerprint": actual == expected}
    args.output.write_text(json.dumps(receipt, indent=2) + "\n")
    if actual != expected:
        raise AssertionError("Actual persisted published BPMN authoring contract differs from the frozen fingerprint; see receipt")
    print("Actual recommended published BPMN descriptors match the frozen fingerprint (2/2).")


if __name__ == "__main__":
    main()
