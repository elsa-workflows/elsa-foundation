#261 [start] +2127.0ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."DefinitionId" = @definitionId AND "w"."IsPublished"
ORDER BY "w"."CreatedAt"

#262 [start] +2129.0ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#263 [start] +2142.0ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#264 [start] +2143.8ms RuntimeElsaDbContext -- SaveChanges

#265 [start] +2144.0ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#266 [start] +2147.9ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#267 [start] +2148.0ms ManagementElsaDbContext -- SaveChanges

#268 [start] +2148.1ms ManagementElsaDbContext Reader
INSERT INTO "WorkflowInstances" ("Id", "CorrelationId", "CreatedAt", "Data", "DataCompressionAlgorithm", "DefinitionId", "DefinitionVersionId", "FinishedAt", "IncidentCount", "IsExecuting", "IsSystem", "Name", "ParentWorkflowInstanceId", "Status", "SubStatus", "TenantId", "UpdatedAt", "Version")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17);


#269 [start] +2149.2ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#270 [start] +2149.5ms RuntimeElsaDbContext -- SaveChanges

#271 [start] +2149.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#272 [start,BACKGROUND] +2150.0ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#273 [start,BACKGROUND] +2150.1ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#274 [start] +2150.6ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#275 [start] +2150.6ms ManagementElsaDbContext -- SaveChanges

#276 [start] +2151.4ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#277 [start] +2152.9ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#278 [start] +2153.2ms RuntimeElsaDbContext -- SaveChanges

#279 [start] +2153.3ms RuntimeElsaDbContext -- TransactionStarted

#280 [start] +2153.3ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#281 [start] +2153.4ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#282 [start] +2153.5ms RuntimeElsaDbContext -- TransactionCommitted

#283 [start] +2154.3ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#284 [start] +2154.4ms ManagementElsaDbContext -- SaveChanges

#285 [start] +2154.5ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#286 [start] +2155.4ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#287 [start] +2155.7ms RuntimeElsaDbContext -- SaveChanges

#288 [start] +2155.7ms RuntimeElsaDbContext -- TransactionStarted

#289 [start] +2155.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#290 [start] +2155.8ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#291 [start] +2155.9ms RuntimeElsaDbContext -- TransactionCommitted

#292 [start] +2156.8ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#293 [start] +2156.9ms ManagementElsaDbContext -- SaveChanges

#294 [start] +2157.0ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#295 [start] +2158.6ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#296 [start] +2159.0ms RuntimeElsaDbContext -- SaveChanges

#297 [start] +2159.0ms RuntimeElsaDbContext -- TransactionStarted

#298 [start] +2159.0ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#299 [start] +2159.1ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#300 [start] +2159.2ms RuntimeElsaDbContext -- TransactionCommitted

#301 [start] +2160.3ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#302 [start] +2160.4ms ManagementElsaDbContext -- SaveChanges

#303 [start] +2160.5ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#304 [start] +2161.2ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#305 [start] +2161.6ms RuntimeElsaDbContext -- SaveChanges

#306 [start] +2161.6ms RuntimeElsaDbContext -- TransactionStarted

#307 [start] +2161.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#308 [start] +2161.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#309 [start] +2161.8ms RuntimeElsaDbContext -- TransactionCommitted

#310 [start] +2162.6ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#311 [start] +2162.6ms ManagementElsaDbContext -- SaveChanges

#312 [start] +2162.7ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#313 [start] +2163.2ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#314 [start] +2163.5ms RuntimeElsaDbContext -- SaveChanges

#315 [start] +2163.5ms RuntimeElsaDbContext -- TransactionStarted

#316 [start] +2163.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#317 [start] +2163.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#318 [start] +2163.7ms RuntimeElsaDbContext -- TransactionCommitted

#319 [start] +2164.4ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#320 [start] +2164.4ms ManagementElsaDbContext -- SaveChanges

#321 [start] +2164.4ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#322 [start] +2164.9ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#323 [start] +2165.1ms RuntimeElsaDbContext -- SaveChanges

#324 [start] +2165.2ms RuntimeElsaDbContext -- TransactionStarted

#325 [start] +2165.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#326 [start] +2165.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#327 [start] +2165.4ms RuntimeElsaDbContext -- TransactionCommitted

#328 [start] +2166.0ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#329 [start] +2166.0ms ManagementElsaDbContext -- SaveChanges

#330 [start] +2166.1ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#331 [start] +2166.6ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#332 [start] +2166.8ms RuntimeElsaDbContext -- SaveChanges

#333 [start] +2166.9ms RuntimeElsaDbContext -- TransactionStarted

#334 [start] +2166.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#335 [start] +2166.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#336 [start] +2167.0ms RuntimeElsaDbContext -- TransactionCommitted

#337 [start] +2167.8ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#338 [start] +2167.8ms ManagementElsaDbContext -- SaveChanges

#339 [start] +2167.9ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#340 [start] +2168.4ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#341 [start] +2168.6ms RuntimeElsaDbContext -- SaveChanges

#342 [start] +2168.7ms RuntimeElsaDbContext -- TransactionStarted

#343 [start] +2168.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#344 [start] +2168.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#345 [start] +2168.9ms RuntimeElsaDbContext -- TransactionCommitted

#346 [start] +2169.6ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#347 [start] +2169.6ms ManagementElsaDbContext -- SaveChanges

#348 [start] +2169.7ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#349 [start] +2170.2ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#350 [start] +2170.5ms RuntimeElsaDbContext -- SaveChanges

#351 [start] +2170.6ms RuntimeElsaDbContext -- TransactionStarted

#352 [start] +2170.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#353 [start] +2170.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#354 [start] +2170.7ms RuntimeElsaDbContext -- TransactionCommitted

#355 [start] +2171.4ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#356 [start] +2171.5ms ManagementElsaDbContext -- SaveChanges

#357 [start] +2171.6ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#358 [start] +2172.3ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47), (@p48, @p49, @p50, @p51, @p52, @p53, @p54, @p55, @p56, @p57, @p58, @p59, @p60, @p61, @p62, @p63, @p64, @p65, @p66, @p67, @p68, @p69, @p70, @p71)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#359 [start] +2172.6ms RuntimeElsaDbContext -- SaveChanges

#360 [start] +2172.6ms RuntimeElsaDbContext -- TransactionStarted

#361 [start] +2172.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#362 [start] +2172.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#363 [start] +2172.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#364 [start] +2172.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#365 [start] +2172.8ms RuntimeElsaDbContext -- TransactionCommitted

#366 [start] +2173.4ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#367 [start] +2173.5ms ManagementElsaDbContext -- SaveChanges

#368 [start] +2173.6ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#369 [start] +2174.5ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'eb6bd7c1a969d601')

#370 [start] +2174.5ms ManagementElsaDbContext -- SaveChanges

#371 [start] +2174.6ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#372 [start,BACKGROUND] +2652.6ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#373 [start,BACKGROUND] +2652.8ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

