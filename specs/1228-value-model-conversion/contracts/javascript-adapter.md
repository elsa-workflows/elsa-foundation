# Contract: JavaScript adapter (JSON profile)

Version: the first pinned version of the JSON profile (ADR 0046 V12). Any change ships as a new version.

## Into JavaScript

| Core value | JavaScript value | Loss |
|---|---|---|
| Object | Plain, changeable object; key order and casing kept | none |
| Array | Real, changeable array | none |
| String (dates included) | String | none |
| Boolean, null | Boolean, null | none |
| Integer within ±(2^53 − 1) | Number | none |
| Integer beyond ±(2^53 − 1), decimal with more digits than a double holds | Number (double) | lossy; recorded at entry |
| Absent | `undefined` | S-2 rules |

No .NET object, type or member is reachable.

## Out of JavaScript

| What the script did | What is written |
|---|---|
| Read only | Nothing |
| Changed a member | That member; the others keep their stored text |
| Assigned a whole value | The whole value |
| Changed an array | The whole array |
| Wrote a value equal to what it read | Nothing (accepted gap) |
| Returned a value | The result, as JSON |

Notation may change (`2.0` → `2`, `1e21` → `1e+21`); values do not.

## Refusals and diagnostics

| Case | Default | Diagnostic |
|---|---|---|
| A lossy number reaches a write | Refused (policy) | Path, stored, written; "JavaScript cannot hold this number exactly: it exceeds 9007199254740991"; remedy: hold it as a string |
| A lossy element in a written array | Refused (policy) | Array path, "written as a whole", each lossy element with both values, remedy |
| A lossy number only computed with | Warning | Path and stored value |
| A lossy number passed to a host function | Logged warning | Function, argument path |
| Node accepts precision loss | Allowed | Instance diagnostic per lossy path |
