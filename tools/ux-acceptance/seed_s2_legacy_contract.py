"""Seed one old required-with-default contract in the runner-owned disposable store, never a real backend."""
import base64
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import sys


def seed(root, draft_id, member_key):
    root = Path(root).resolve(strict=True)
    owned = os.environ.get("UX_ACCEPTANCE_OWNED_ROOT")
    if os.environ.get("UX_ACCEPTANCE_OWNED") != "1" or not owned or root != Path(owned).resolve(strict=True):
        raise ValueError("Legacy seeding requires the exact runner-owned disposable root")
    encoded = base64.b64encode(draft_id.encode("utf-16-le")).decode("ascii")
    candidates = []
    for path in root.rglob("*.db"):
        path = path.resolve(strict=True)
        if not path.is_relative_to(root): raise ValueError("A database escaped the owned root")
        with sqlite3.connect(path, timeout=30) as db:
            names = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            if "elsa_activity_definition_drafts" not in names: continue
            rows = db.execute('SELECT "TenantScopeKey", "IdIdentityHash", "State", "Revision" FROM "elsa_activity_definition_drafts" WHERE "Id" IN (?,?)', (draft_id, encoded)).fetchall()
            candidates += [(path, row) for row in rows]
    if len(candidates) != 1: raise ValueError(f"Expected one owned draft, found {len(candidates)}")
    path, (tenant, identity, raw, revision) = candidates[0]
    state = json.loads(raw)
    def key(value, name): return next(name_ for name_ in value if name_.lower() == name.lower())
    contract = state[key(state, "contract")]
    members = contract[key(contract, "inputs")]
    encoded_member = base64.b64encode(member_key.encode("utf-16-le")).decode("ascii")
    selected = [member for member in members if member[key(member, "referenceKey")] in (member_key, encoded_member)]
    if len(selected) != 1: raise ValueError("Expected one exact contract member")
    member = selected[0]
    if member.get(key(member, "default")) is None or member[key(member, "isRequired")] is not False:
        raise ValueError("Seed only a newly created optional member with a declared default")
    member[key(member, "isRequired")] = True
    changed = json.dumps(state, ensure_ascii=True, separators=(",", ":"))
    with sqlite3.connect(path, timeout=30) as db:
        count = db.execute('UPDATE "elsa_activity_definition_drafts" SET "State"=?, "Revision"=? WHERE "TenantScopeKey"=? AND "IdIdentityHash"=? AND "Revision"=? AND "State"=?',
                           (changed, revision + 1, tenant, identity, revision, raw)).rowcount
        if count != 1: raise ValueError("The owned legacy seed lost its exact revision")
    return {"fixture": "old required-with-default member", "database": str(path.relative_to(root)), "draftId": draft_id,
            "beforeRevision": revision, "afterRevision": revision + 1,
            "beforeSha256": hashlib.sha256(raw.encode()).hexdigest(), "afterSha256": hashlib.sha256(changed.encode()).hexdigest()}


if __name__ == "__main__":
    print(json.dumps(seed(*sys.argv[1:])))
