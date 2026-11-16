#261 [request] +2124.1ms RuntimeElsaDbContext Reader
SELECT "t"."Id", "t"."ActivityId", "t"."Hash", "t"."Name", "t"."SerializedPayload", "t"."TenantId", "t"."WorkflowDefinitionId", "t"."WorkflowDefinitionVersionId"
FROM "Triggers" AS "t"
WHERE (@ef_filter__p4 OR "t"."TenantId" = @ef_filter__TenantId OR "t"."TenantId" = '*' OR ("t"."TenantId" IS NULL AND @ef_filter__p3)) AND "t"."Hash" = @Hash

#262 [request] +2127.1ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#263 [request] +2152.1ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#264 [request] +2154.4ms RuntimeElsaDbContext -- SaveChanges

#265 [request] +2154.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#266 [request] +2159.0ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '320e8d96d6e63675')

#267 [request] +2159.2ms ManagementElsaDbContext -- SaveChanges

#268 [request] +2159.2ms ManagementElsaDbContext Reader
INSERT INTO "WorkflowInstances" ("Id", "CorrelationId", "CreatedAt", "Data", "DataCompressionAlgorithm", "DefinitionId", "DefinitionVersionId", "FinishedAt", "IncidentCount", "IsExecuting", "IsSystem", "Name", "ParentWorkflowInstanceId", "Status", "SubStatus", "TenantId", "UpdatedAt", "Version")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17);


#269 [request] +2160.1ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#270 [request] +2160.5ms RuntimeElsaDbContext -- SaveChanges

#271 [request] +2160.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#272 [request] +2161.7ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '320e8d96d6e63675')

#273 [request] +2161.8ms ManagementElsaDbContext -- SaveChanges

#274 [request,BACKGROUND] +2161.8ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#275 [request] +2162.0ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#276 [request,BACKGROUND] +2162.1ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#277 [request] +2163.3ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#278 [request] +2163.6ms RuntimeElsaDbContext -- SaveChanges

#279 [request] +2163.6ms RuntimeElsaDbContext -- TransactionStarted

#280 [request] +2163.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#281 [request] +2163.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#282 [request] +2163.8ms RuntimeElsaDbContext -- TransactionCommitted

#283 [request] +2164.7ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '320e8d96d6e63675')

#284 [request] +2164.8ms ManagementElsaDbContext -- SaveChanges

#285 [request] +2164.8ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#286 [request] +2166.3ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#287 [request] +2166.6ms RuntimeElsaDbContext -- SaveChanges

#288 [request] +2166.6ms RuntimeElsaDbContext -- TransactionStarted

#289 [request] +2166.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#290 [request] +2166.7ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#291 [request] +2166.8ms RuntimeElsaDbContext -- TransactionCommitted

#292 [request] +2167.7ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '320e8d96d6e63675')

#293 [request] +2167.7ms ManagementElsaDbContext -- SaveChanges

#294 [request] +2167.8ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#295 [request] +2168.9ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47), (@p48, @p49, @p50, @p51, @p52, @p53, @p54, @p55, @p56, @p57, @p58, @p59, @p60, @p61, @p62, @p63, @p64, @p65, @p66, @p67, @p68, @p69, @p70, @p71)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#296 [request] +2169.1ms RuntimeElsaDbContext -- SaveChanges

#297 [request] +2169.2ms RuntimeElsaDbContext -- TransactionStarted

#298 [request] +2169.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#299 [request] +2169.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#300 [request] +2169.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#301 [request] +2169.3ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#302 [request] +2169.4ms RuntimeElsaDbContext -- TransactionCommitted

#303 [request] +2170.1ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '320e8d96d6e63675')

#304 [request] +2170.1ms ManagementElsaDbContext -- SaveChanges

#305 [request] +2170.1ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#306 [request] +2170.9ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '320e8d96d6e63675')

#307 [request] +2171.0ms ManagementElsaDbContext -- SaveChanges

#308 [request] +2171.0ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#309 [request,BACKGROUND] +2665.8ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#310 [request,BACKGROUND] +2666.1ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

