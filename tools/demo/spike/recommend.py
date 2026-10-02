#!/usr/bin/env python3
"""Spike only: makes a catalog activity version the recommended one of its definition, which is the version the designer's
palette offers for a new node (PUT /design/activities/definitions/{id}/recommendation, the call Studio's activity
definition page makes). A new version contributed by a package becomes the definition's head, never its recommendation
by itself: nodes already placed stay pinned to their version, and new ones get what an author recommended.

    python3 tools/demo/spike/recommend.py TYPE_KEY_SUFFIX VERSION [--base URL] [--token FILE]
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.request

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
parser = argparse.ArgumentParser()
parser.add_argument("type_key")
parser.add_argument("version")
parser.add_argument("--base", default="http://localhost:7211")
parser.add_argument("--token", default=os.path.join(root, "artifacts/spike/hosts/wb/.token"))
args = parser.parse_args()
token = open(args.token).read().strip()


def call(method, path, body=None):
    request = urllib.request.Request(args.base + path, data=None if body is None else json.dumps(body).encode(), method=method,
                                     headers={"Authorization": "Bearer " + token, "Accept": "application/json",
                                              "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        print(f"{method} {path} -> HTTP {error.code}\n{error.read().decode()[:2000]}", file=sys.stderr)
        sys.exit(1)


catalog = call("GET", "/design/activities/catalog")
items = catalog if isinstance(catalog, list) else next((v for v in catalog.values() if isinstance(v, list)), [])
target = next((i for i in items if i["activityTypeKey"].endswith(args.type_key) and i["version"] == args.version), None)
if target is None:
    sys.exit(f"no catalog entry for *{args.type_key} {args.version}")
definition = call("GET", f"/design/activities/versions/{target['activityVersionId']}")["definition"]
view = call("PUT", f"/design/activities/definitions/{definition['definitionId']}/recommendation", {
    "expectedDefinitionHeadVersionId": definition["headVersionId"],
    "expectedRecommendedVersionId": definition["recommendedVersionId"],
    "recommendedVersionId": target["activityVersionId"],
    "expectedRecommendedVersionLifecycle": "Active",
    "reason": f"Release {args.version} of the package is installed"})
print(f"{target['displayName']}: recommended version is now {args.version} ({view.get('recommendedVersionId')})")
