#!/usr/bin/env python3
"""Identify the exact image Docker saved and loaded across image stores."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import tarfile


DIGEST = re.compile(r"sha256:[a-f0-9]{64}\Z")


def sha256(data):
    return "sha256:" + hashlib.sha256(data).hexdigest()


def saved_image_ids(path, tag):
    with tarfile.open(path, "r:gz") as archive:
        rows = json.load(archive.extractfile("manifest.json"))
        if len(rows) != 1 or rows[0]["RepoTags"] != [tag]:
            raise ValueError("Archive must contain exactly the qualified image tag")
        config_id = sha256(archive.extractfile(rows[0]["Config"]).read())
        ids = {config_id}
        if "index.json" in archive.getnames():
            index = json.load(archive.extractfile("index.json"))
            for entry in index["manifests"]:
                ids.update(oci_image_ids(archive, entry, config_id))
    return ids


def oci_image_ids(archive, descriptor, config_id):
    digest = descriptor["digest"]
    if not DIGEST.fullmatch(digest):
        raise ValueError("Saved OCI manifest digest is invalid")
    contents = archive.extractfile("blobs/" + digest.replace(":", "/", 1)).read()
    if sha256(contents) != digest:
        raise ValueError("Saved OCI manifest content does not match its digest")
    document = json.loads(contents)
    if "config" in document:
        return {digest} if document["config"]["digest"] == config_id else set()

    # Buildx can save an index carrying one runtime image and its attestations.
    children = document["manifests"]
    runtime = [entry for entry in children if not is_attestation(entry)]
    if len(runtime) != 1:
        return set()
    if any(entry["annotations"].get("vnd.docker.reference.digest") != runtime[0]["digest"]
           for entry in children if is_attestation(entry)):
        return set()
    aliases = oci_image_ids(archive, runtime[0], config_id)
    return {digest, *aliases} if aliases else set()


def is_attestation(entry):
    platform = entry.get("platform", {})
    return (platform.get("os") == platform.get("architecture") == "unknown"
            and entry.get("annotations", {}).get("vnd.docker.reference.type") == "attestation-manifest")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("tag")
    parser.add_argument("qualified_id")
    parser.add_argument("loaded_id")
    args = parser.parse_args()
    try:
        if not {args.qualified_id, args.loaded_id} <= saved_image_ids(args.archive, args.tag):
            raise ValueError("The archive does not bind the qualified and loaded image IDs to the same config")
    except (OSError, ValueError, KeyError, tarfile.TarError) as error:
        parser.exit(1, f"Server profile archive verification failed: {error}\n")


if __name__ == "__main__":
    main()
