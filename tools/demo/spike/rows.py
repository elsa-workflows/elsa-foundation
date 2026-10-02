#!/usr/bin/env python3
"""Spike only: the Notes table of a Sqlite notes database, one line per note: its text, the schema version it was stamped
with, and its tags as stored (as tools/demo/helpers.sh `rows solo` shows them).

    python3 tools/demo/spike/rows.py [DB]   (default artifacts/spike/hosts/wb/notes.db)
"""
import os
import sqlite3
import sys

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
db = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "artifacts/spike/hosts/wb/notes.db")
connection = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
columns = [row[1] for row in connection.execute("PRAGMA table_info(elsa_samples_notes)")]
tags = next((c for c in columns if c.lower() in ("tagsjson", "tags_json", "tags")), None)
text = next(c for c in columns if c.lower() == "text")
stamp = next(c for c in columns if c.lower() in ("schemaversion", "schema_version"))
created = next(c for c in columns if c.lower() in ("createdat", "created_at"))
query = f"SELECT {text}, {stamp}, {tags if tags else 'NULL'} FROM elsa_samples_notes ORDER BY {created}"
print(f"{'note':40} {'schema':7} tags")
for note, version, value in connection.execute(query):
    print(f"{note:40} {version:7} {value if tags else '(no column yet)'}")
