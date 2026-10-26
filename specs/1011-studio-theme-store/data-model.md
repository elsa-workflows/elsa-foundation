# Theme Store data boundaries

- **Installed options/settings**: one validated copied source policy and three independent absolute paths plus optional configured default. Root-owned, restart to change.
- **Managed state**: complete stored custom definitions, stored default and disabled built-in IDs. Historical managed records survive source changes. Stored JSON stays wire-compatible.
- **Directory collection**: read-only external definitions loaded from a configured collection file or JSON directory; reserved built-in IDs are skipped. Directory IDs take precedence in reads.
- **Read snapshot**: managed state plus directory definitions/provenance and the merged view; persistence must use the managed member, never the merged view.
- **Response**: merged inventory, projected available default, material-asset metadata, source mode and directory IDs. Asset URLs and derived policy/provenance are not persisted.
- **Mutation**: under the shared host lease, read snapshot, validate intent, update only managed state or owned assets, atomically commit, and rebuild the response.

Existing theme definition/palette/material/typography/shape/layout and version1 import/export JSON formats remain unchanged.
