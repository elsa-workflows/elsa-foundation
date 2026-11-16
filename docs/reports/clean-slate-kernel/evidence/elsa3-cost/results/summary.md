# Elsa 3 command counts (sqlite)

| Workflow | Config | Runs | Until the call returned (Elsa 4 rule) | Settled window per run | In-flow | Background | SaveChanges | Transactions | Per phase (settled) |
|---|---|---|---|---|---|---|---|---|---|
| writeline-sequence | default | 3 | 29, 29, 29 | 33, 33, 33 (min 33, max 33) | 29, 29, 29 | 4, 4, 4 | 2, 2, 2 | 1, 1, 1 | start 33 |
| writeline-sequence | activity-executed | 3 | 66, 66, 66 | 68, 68, 68 (min 68, max 68) | 64, 64, 64 | 4, 4, 4 | 25, 25, 25 | 10, 10, 10 | start 68 |
| writeline-sequence | default-cache | 3 | 29, 29, 29 | 33, 33, 33 (min 33, max 33) | 29, 29, 29 | 4, 4, 4 | 2, 2, 2 | 1, 1, 1 | start 33 |
| http-reference | default | 3 | 15, 15, 15 | 19, 19, 19 (min 19, max 19) | 15, 15, 15 | 4, 4, 4 | 2, 2, 2 | 1, 1, 1 | request 19 |
| http-reference | activity-executed | 3 | 31, 31, 31 | 33, 33, 33 (min 33, max 33) | 29, 29, 29 | 4, 4, 4 | 11, 11, 11 | 3, 3, 3 | request 33 |
| http-reference | default-cache | 3 | 15, 15, 15 | 19, 19, 19 (min 19, max 19) | 15, 15, 15 | 4, 4, 4 | 2, 2, 2 | 1, 1, 1 | request 19 |
| fork-join-resume | default | 3 | 25, 25, 25 | 33, 33, 33 (min 33, max 33) | 25, 25, 25 | 8, 8, 8 | 5, 5, 5 | 2, 2, 2 | resume 16; start 17 |
| fork-join-resume | activity-executed | 3 | 45, 46, 45 | 50, 50, 50 (min 50, max 50) | 42, 42, 42 | 8, 8, 8 | 15, 15, 15 | 2, 2, 2 | resume 20; start 30 |
| fork-join-resume | default-cache | 3 | 24, 24, 24 | 32, 32, 32 (min 32, max 32) | 24, 24, 24 | 8, 8, 8 | 5, 5, 5 | 2, 2, 2 | resume 15; start 17 |

## writeline-sequence / default (run 1: 33 commands)

Phases: start: 29 until the call returned, 4 after

By DbContext: ManagementElsaDbContext 4, RuntimeElsaDbContext 29  
By command kind: NonQuery 1, Reader 32  
By lead operation: INSERT 26, SELECT 7  
SaveChanges by context/phase: ManagementElsaDbContext start 1, RuntimeElsaDbContext start 1

| Category (commands) | Commands |
|---|---|
| activity execution records | 1 |
| bookmark queue | 4 |
| definitions | 2 |
| execution log | 24 |
| instance state | 2 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 2 |
| ManagementElsaDbContext SELECT WorkflowInstances | 1 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 24 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 4 |

## writeline-sequence / activity-executed (run 1: 68 commands)

Phases: start: 66 until the call returned, 2 after

By DbContext: ManagementElsaDbContext 28, RuntimeElsaDbContext 40  
By command kind: NonQuery 12, Reader 56  
By lead operation: INSERT 37, SELECT 19, UPDATE 12  
SaveChanges by context/phase: ManagementElsaDbContext start 13, RuntimeElsaDbContext start 12

| Category (commands) | Commands |
|---|---|
| activity execution records | 12 |
| bookmark queue | 4 |
| definitions | 2 |
| execution log | 24 |
| instance state | 26 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 2 |
| ManagementElsaDbContext SELECT WorkflowInstances | 13 |
| ManagementElsaDbContext UPDATE WorkflowInstances | 12 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 12 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 24 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 4 |

## writeline-sequence / default-cache (run 1: 33 commands)

Phases: start: 29 until the call returned, 4 after

By DbContext: ManagementElsaDbContext 4, RuntimeElsaDbContext 29  
By command kind: NonQuery 1, Reader 32  
By lead operation: INSERT 26, SELECT 7  
SaveChanges by context/phase: ManagementElsaDbContext start 1, RuntimeElsaDbContext start 1

| Category (commands) | Commands |
|---|---|
| activity execution records | 1 |
| bookmark queue | 4 |
| definitions | 2 |
| execution log | 24 |
| instance state | 2 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 2 |
| ManagementElsaDbContext SELECT WorkflowInstances | 1 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 24 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 4 |

## http-reference / default (run 1: 19 commands)

Phases: request: 15 until the call returned, 4 after

By DbContext: ManagementElsaDbContext 3, RuntimeElsaDbContext 16  
By command kind: NonQuery 1, Reader 18  
By lead operation: INSERT 12, SELECT 7  
SaveChanges by context/phase: ManagementElsaDbContext request 1, RuntimeElsaDbContext request 1

| Category (commands) | Commands |
|---|---|
| activity execution records | 1 |
| bookmark queue | 4 |
| definitions | 1 |
| execution log | 10 |
| instance state | 2 |
| triggers | 1 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 1 |
| ManagementElsaDbContext SELECT WorkflowInstances | 1 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 10 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 4 |
| RuntimeElsaDbContext SELECT Triggers | 1 |

## http-reference / activity-executed (run 1: 33 commands)

Phases: request: 31 until the call returned, 2 after

By DbContext: ManagementElsaDbContext 13, RuntimeElsaDbContext 20  
By command kind: NonQuery 5, Reader 28  
By lead operation: INSERT 16, SELECT 12, UPDATE 5  
SaveChanges by context/phase: ManagementElsaDbContext request 6, RuntimeElsaDbContext request 5

| Category (commands) | Commands |
|---|---|
| activity execution records | 5 |
| bookmark queue | 4 |
| definitions | 1 |
| execution log | 10 |
| instance state | 12 |
| triggers | 1 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 1 |
| ManagementElsaDbContext SELECT WorkflowInstances | 6 |
| ManagementElsaDbContext UPDATE WorkflowInstances | 5 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 5 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 10 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 4 |
| RuntimeElsaDbContext SELECT Triggers | 1 |

## http-reference / default-cache (run 1: 19 commands)

Phases: request: 15 until the call returned, 4 after

By DbContext: ManagementElsaDbContext 3, RuntimeElsaDbContext 16  
By command kind: NonQuery 1, Reader 18  
By lead operation: INSERT 12, SELECT 7  
SaveChanges by context/phase: ManagementElsaDbContext request 1, RuntimeElsaDbContext request 1

| Category (commands) | Commands |
|---|---|
| activity execution records | 1 |
| bookmark queue | 4 |
| definitions | 1 |
| execution log | 10 |
| instance state | 2 |
| triggers | 1 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 1 |
| ManagementElsaDbContext SELECT WorkflowInstances | 1 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 10 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 4 |
| RuntimeElsaDbContext SELECT Triggers | 1 |

## fork-join-resume / default (run 1: 33 commands)

Phases: start: 13 until the call returned, 4 after; resume: 12 until the call returned, 4 after

By DbContext: ManagementElsaDbContext 8, RuntimeElsaDbContext 25  
By command kind: NonQuery 3, Reader 30  
By lead operation: DELETE 1, INSERT 14, SELECT 17, UPDATE 1  
SaveChanges by context/phase: ManagementElsaDbContext resume 1, ManagementElsaDbContext start 1, RuntimeElsaDbContext resume 1, RuntimeElsaDbContext start 2

| Category (commands) | Commands |
|---|---|
| activity execution records | 2 |
| bookmark queue | 8 |
| bookmarks | 5 |
| definitions | 3 |
| execution log | 10 |
| instance state | 5 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 3 |
| ManagementElsaDbContext SELECT WorkflowInstances | 3 |
| ManagementElsaDbContext UPDATE WorkflowInstances | 1 |
| RuntimeElsaDbContext DELETE Bookmarks | 1 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 2 |
| RuntimeElsaDbContext INSERT Bookmarks | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 10 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 8 |
| RuntimeElsaDbContext SELECT Bookmarks | 3 |

## fork-join-resume / activity-executed (run 1: 50 commands)

Phases: start: 28 until the call returned, 2 after; resume: 17 until the call returned, 3 after

By DbContext: ManagementElsaDbContext 18, RuntimeElsaDbContext 32  
By command kind: NonQuery 7, Reader 43  
By lead operation: DELETE 2, INSERT 17, SELECT 24, UPDATE 7  
SaveChanges by context/phase: ManagementElsaDbContext resume 2, ManagementElsaDbContext start 5, RuntimeElsaDbContext resume 1, RuntimeElsaDbContext start 7

| Category (commands) | Commands |
|---|---|
| activity execution records | 5 |
| bookmark queue | 8 |
| bookmarks | 9 |
| definitions | 3 |
| execution log | 10 |
| instance state | 15 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 3 |
| ManagementElsaDbContext SELECT WorkflowInstances | 8 |
| ManagementElsaDbContext UPDATE WorkflowInstances | 6 |
| RuntimeElsaDbContext DELETE Bookmarks | 2 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 5 |
| RuntimeElsaDbContext INSERT Bookmarks | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 10 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 8 |
| RuntimeElsaDbContext SELECT Bookmarks | 5 |
| RuntimeElsaDbContext UPDATE Bookmarks | 1 |

## fork-join-resume / default-cache (run 1: 32 commands)

Phases: start: 13 until the call returned, 4 after; resume: 11 until the call returned, 4 after

By DbContext: ManagementElsaDbContext 7, RuntimeElsaDbContext 25  
By command kind: NonQuery 3, Reader 29  
By lead operation: DELETE 1, INSERT 14, SELECT 16, UPDATE 1  
SaveChanges by context/phase: ManagementElsaDbContext resume 1, ManagementElsaDbContext start 1, RuntimeElsaDbContext resume 1, RuntimeElsaDbContext start 2

| Category (commands) | Commands |
|---|---|
| activity execution records | 2 |
| bookmark queue | 8 |
| bookmarks | 5 |
| definitions | 2 |
| execution log | 10 |
| instance state | 5 |

| Statements: context, op, table | Statements |
|---|---|
| ManagementElsaDbContext INSERT WorkflowInstances | 1 |
| ManagementElsaDbContext SELECT WorkflowDefinitions | 2 |
| ManagementElsaDbContext SELECT WorkflowInstances | 3 |
| ManagementElsaDbContext UPDATE WorkflowInstances | 1 |
| RuntimeElsaDbContext DELETE Bookmarks | 1 |
| RuntimeElsaDbContext INSERT ActivityExecutionRecords | 2 |
| RuntimeElsaDbContext INSERT Bookmarks | 1 |
| RuntimeElsaDbContext INSERT WorkflowExecutionLogRecords | 10 |
| RuntimeElsaDbContext SELECT BookmarkQueueItems | 8 |
| RuntimeElsaDbContext SELECT Bookmarks | 3 |
