#!/usr/bin/env python3
"""Spike only: prints the activity catalog entries (and their inputs) whose type key or name matches a filter.

    python3 tools/demo/spike/catalog.py [FILTER] [BASE_URL] [TOKEN_FILE]
"""
import json
import os
import sys
import urllib.request

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
needle = (sys.argv[1] if len(sys.argv) > 1 else "Nuplane").lower()
base = sys.argv[2] if len(sys.argv) > 2 else "http://localhost:7211"
token_file = sys.argv[3] if len(sys.argv) > 3 else os.path.join(root, "artifacts/spike/hosts/wb/.token")
token = open(token_file).read().strip()


def get(path):
    request = urllib.request.Request(base + path, headers={"Authorization": "Bearer " + token, "Accept": "application/json"})
    with urllib.request.urlopen(request) as response:
        return json.load(response)


catalog = get("/design/activities/catalog")
items = catalog if isinstance(catalog, list) else next((v for v in catalog.values() if isinstance(v, list)), [])
print(f"catalog: {len(items)} entries")
for item in items:
    text = json.dumps(item)
    if needle in text.lower():
        print(json.dumps(item, indent=1)[:4000])
