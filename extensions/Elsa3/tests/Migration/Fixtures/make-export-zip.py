"""Rebuilds export.zip from export/ (spec 1237 T002). Run from anywhere: python make-export-zip.py

Every file under export/ becomes an entry under an export/ root, in ordinal order of the entry path, with a fixed
timestamp, so the zip changes only when the folder does.
"""
import os
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
EXPORT = os.path.join(HERE, "export")

entries = []
for directory, _, files in os.walk(EXPORT):
    for name in files:
        full = os.path.join(directory, name)
        entries.append(("export/" + os.path.relpath(full, EXPORT).replace(os.sep, "/"), full))
entries.sort(key=lambda entry: entry[0].encode("utf-8"))

with zipfile.ZipFile(os.path.join(HERE, "export.zip"), "w", zipfile.ZIP_DEFLATED) as archive:
    for name, full in entries:
        info = zipfile.ZipInfo(name, date_time=(2025, 3, 14, 9, 26, 52))
        info.compress_type = zipfile.ZIP_DEFLATED
        with open(full, "rb") as handle:
            archive.writestr(info, handle.read())

print(f"export.zip: {len(entries)} entries")
