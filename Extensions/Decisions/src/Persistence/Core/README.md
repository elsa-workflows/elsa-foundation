# Decisions persistence contracts

The independent reader and writer enforce tenant scope, expected-revision changes, immutable publications and exact operation replay. Mutations return confirmations; callers query snapshots separately. Completed test receipts retain safe comparison summaries unless the host explicitly enables protected history. This store contains no pending execution, scheduler, lease or recovery state. Future durable decision execution uses the existing shared model host.
