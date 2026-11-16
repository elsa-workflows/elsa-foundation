# Normal identity-provider qualification

The normal Workbench at `2c8925a2ac808db26c6cb1b051e534b3736afbfd` passed the 37 assertions in `receipt.json`. Seven restricted cookie-authenticated users across two tenants exercised independent decision grants, protected source/results, Sequence/Flowchart/BPMN workflow-only redaction, exact source exports and Evaluate revocation after a real process restart. The earlier denied request redirected to the access-denied page; endpoint-specific cookie redirect suppression corrected the observed wire behavior to401/403.

Reproduce from a clean checkout with Studio dependencies installed:

```sh
python3 tools/decisions/qualify-execution.py --output /tmp/decisions-execution-and-permissions
```

The runner owns fresh host processes, home and SQLite stores. The private provisioner compiles against the exact copied host assemblies and uses the owning Identity provider registration, UserManager, role manager and membership stores. It neither invents normalized claims nor changes schemas. The ordinary host applies its own migrations first. Role revocation uses the real revision-aware role store; subsequent execution authorization reprojects current persisted grants.

Templates become an actual project only outside the checkout. Credentials and private requests remain in the disposable directory with mode0600 and are deleted with it. Only public receipts and safe diagnostics are retained. No source tree or existing developer store is used for qualification data.

Limits: this default-host run verified decision-dependent workflow outputs; it did not have resolvable captured activity payload evidence, a hosted-model BPMN execution ID or an external identity provider. Those HTTP paths are explicitly unclaimed. The separate workflow disclosure suite tests exact model dependency traversal and protected payload authorization, and unsupported execution identities fail closed.
