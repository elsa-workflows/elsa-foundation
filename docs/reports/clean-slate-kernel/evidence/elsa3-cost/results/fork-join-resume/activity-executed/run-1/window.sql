#261 [start] +2150.7ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."DefinitionId" = @definitionId AND "w"."IsPublished"
ORDER BY "w"."CreatedAt"

#262 [start] +2153.4ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#263 [start] +2172.1ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#264 [start] +2174.3ms RuntimeElsaDbContext -- SaveChanges

#265 [start] +2174.4ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#266 [start] +2178.7ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#267 [start] +2178.8ms ManagementElsaDbContext -- SaveChanges

#268 [start] +2178.9ms ManagementElsaDbContext Reader
INSERT INTO "WorkflowInstances" ("Id", "CorrelationId", "CreatedAt", "Data", "DataCompressionAlgorithm", "DefinitionId", "DefinitionVersionId", "FinishedAt", "IncidentCount", "IsExecuting", "IsSystem", "Name", "ParentWorkflowInstanceId", "Status", "SubStatus", "TenantId", "UpdatedAt", "Version")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17);


#269 [start] +2179.7ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#270 [start] +2180.0ms RuntimeElsaDbContext -- SaveChanges

#271 [start] +2180.0ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#272 [start] +2180.9ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#273 [start] +2181.0ms ManagementElsaDbContext -- SaveChanges

#274 [start] +2181.0ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#275 [start] +2182.1ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#276 [start,BACKGROUND] +2182.5ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#277 [start] +2182.6ms RuntimeElsaDbContext -- SaveChanges

#278 [start,BACKGROUND] +2182.6ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#279 [start] +2182.7ms RuntimeElsaDbContext -- TransactionStarted

#280 [start] +2182.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#281 [start] +2182.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#282 [start] +2182.9ms RuntimeElsaDbContext -- TransactionCommitted

#283 [start] +2183.7ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#284 [start] +2183.7ms ManagementElsaDbContext -- SaveChanges

#285 [start] +2183.7ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#286 [start] +2185.6ms RuntimeElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "Bookmarks" AS "b"
    WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = '253bf9cf97e15932')

#287 [start] +2185.7ms RuntimeElsaDbContext -- SaveChanges

#288 [start] +2185.7ms RuntimeElsaDbContext Reader
INSERT INTO "Bookmarks" ("Id", "ActivityInstanceId", "ActivityTypeName", "CorrelationId", "CreatedAt", "Hash", "Name", "SerializedMetadata", "SerializedPayload", "TenantId", "WorkflowInstanceId")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10);


#289 [start] +2186.0ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#290 [start] +2186.3ms RuntimeElsaDbContext -- SaveChanges

#291 [start] +2186.3ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#292 [start] +2187.5ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#293 [start] +2187.5ms ManagementElsaDbContext -- SaveChanges

#294 [start] +2187.5ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#295 [start] +2187.9ms RuntimeElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "Bookmarks" AS "b"
    WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = '253bf9cf97e15932')

#296 [start] +2187.9ms RuntimeElsaDbContext -- SaveChanges

#297 [start] +2188.0ms RuntimeElsaDbContext Reader
UPDATE "Bookmarks" SET "ActivityInstanceId" = @p0, "ActivityTypeName" = @p1, "CorrelationId" = @p2, "CreatedAt" = @p3, "Hash" = @p4, "Name" = @p5, "SerializedMetadata" = @p6, "SerializedPayload" = @p7, "TenantId" = @p8, "WorkflowInstanceId" = @p9
WHERE "Id" = @p10
RETURNING 1;


#298 [start] +2188.1ms RuntimeElsaDbContext -- SaveChanges

#299 [start] +2188.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#300 [start] +2189.0ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#301 [start] +2189.0ms ManagementElsaDbContext -- SaveChanges

#302 [start] +2189.0ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#303 [start,BACKGROUND] +2686.6ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#304 [start,BACKGROUND] +2687.2ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#307 [resume] +4195.8ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."CorrelationId", "b"."CreatedAt", "b"."Hash", "b"."Name", "b"."SerializedMetadata", "b"."SerializedPayload", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Hash" = @filter_Hash AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId AND "b"."Name" = @filter_Name

#308 [resume] +4199.4ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."CorrelationId", "w"."CreatedAt", "w"."Data", "w"."DataCompressionAlgorithm", "w"."DefinitionId", "w"."DefinitionVersionId", "w"."FinishedAt", "w"."IncidentCount", "w"."IsExecuting", "w"."IsSystem", "w"."Name", "w"."ParentWorkflowInstanceId", "w"."Status", "w"."SubStatus", "w"."TenantId", "w"."UpdatedAt", "w"."Version"
FROM "WorkflowInstances" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @filter_Id

#309 [resume] +4200.0ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#310 [resume] +4203.8ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."CorrelationId", "b"."CreatedAt", "b"."Hash", "b"."Name", "b"."SerializedMetadata", "b"."SerializedPayload", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = @filter_BookmarkIds1 AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId

#311 [resume] +4204.0ms RuntimeElsaDbContext NonQuery
DELETE FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = @filter_BookmarkIds1 AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId

#312 [resume] +4205.0ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47), (@p48, @p49, @p50, @p51, @p52, @p53, @p54, @p55, @p56, @p57, @p58, @p59, @p60, @p61, @p62, @p63, @p64, @p65, @p66, @p67, @p68, @p69, @p70, @p71)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#313 [resume] +4205.4ms RuntimeElsaDbContext -- SaveChanges

#314 [resume] +4205.4ms RuntimeElsaDbContext -- TransactionStarted

#315 [resume] +4205.4ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#316 [resume] +4205.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#317 [resume] +4205.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#318 [resume] +4205.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#319 [resume] +4205.7ms RuntimeElsaDbContext -- TransactionCommitted

#320 [resume] +4206.9ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#321 [resume] +4207.0ms ManagementElsaDbContext -- SaveChanges

#322 [resume] +4207.0ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#323 [resume] +4207.5ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."CorrelationId", "b"."CreatedAt", "b"."Hash", "b"."Name", "b"."SerializedMetadata", "b"."SerializedPayload", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = @filter_BookmarkIds1 AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId

#324 [resume] +4207.6ms RuntimeElsaDbContext NonQuery
DELETE FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = @filter_BookmarkIds1 AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId

#325 [resume] +4208.0ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '4ba7cd571823858b')

#326 [resume] +4208.0ms ManagementElsaDbContext -- SaveChanges

#327 [resume] +4208.1ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#328 [resume,BACKGROUND] +4208.4ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#329 [resume,BACKGROUND] +4208.5ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#330 [resume,BACKGROUND] +4709.7ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#331 [resume,BACKGROUND] +4710.2ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

