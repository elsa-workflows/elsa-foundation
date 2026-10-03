# Toolbox renewals demo

This lane replaces the Notes/Workbench demonstration with packaged renewal activities and an authenticated Foundation.Host composition. Feature implementations and the demo identity bootstrap arrive through Nuplane. `Composition/Composition.csproj` is a preparation-only build graph; Foundation.Host never references it or its feature implementations.

The local React cockpit lives in `/Users/sipke/Documents/Codex/2026-10-03/modules-without-restart/demo-cockpit`, with a presentation alongside it. The cockpit owns its Foundation.Host processes on 5311/5312 and Studio on 5313. It refuses occupied ports and stops only child processes it started. It uses `prepare.py` through a fixed allowlist rather than accepting browser commands or filesystem paths.

Preparation builds source packages, derives their closure from NuGet's actual restored project graph, and copies external package archives from the restored cache. Both demo releases are staged before the presentation. Publication atomically places the two matching renewal packages into one host's local feed. Each host has its own Nuplane install state and feed; both share the isolated renewal database. Source-built host EF metadata is shared rather than loaded a second time.

The baseline configuration uses Validate and disables automatic shell reload on package changes, leaving the reload as an explicit presenter action. Nuplane still acquires packages at startup and watches the feed. Periodic scheduled reconciliation is disabled in this demo: rapid scheduled cycles can accumulate in its FIFO during busy baseline setup and delay a manual request. Both cockpit Publish buttons immediately call the authenticated reconciliation endpoint and wait for both real installed package versions. The cockpit reads the installed release from store state and the served release from the sample's actual release endpoint. Schema dormancy comes from the real premium endpoint; HTTP 404 alone is never called a served release.

Reset stops owned processes, refuses occupied demo ports, then archives the isolated runtime directories and database files. It retains prepared packages. Archived files can be recovered; reset never deletes user data or signals unrelated processes.

The complete 3 October 2026 browser rehearsal passed, including actual Studio execution, explicit node upgrade, preserved data and a live two-host readiness transition. See [verification](../../../specs/191-toolbox-renewals-demo/verification.md) and the external evidence report for the precise scope and limitations.
