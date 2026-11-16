#261 [start] +2230.2ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."DefinitionId" = @definitionId AND "w"."IsPublished"
ORDER BY "w"."CreatedAt"

#262 [start] +2231.2ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#263 [start] +2245.3ms RuntimeElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "Bookmarks" AS "b"
    WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = 'a20f0d0efadce80f')

#264 [start] +2249.8ms RuntimeElsaDbContext -- SaveChanges

#265 [start] +2250.3ms RuntimeElsaDbContext Reader
INSERT INTO "Bookmarks" ("Id", "ActivityInstanceId", "ActivityTypeName", "CorrelationId", "CreatedAt", "Hash", "Name", "SerializedMetadata", "SerializedPayload", "TenantId", "WorkflowInstanceId")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10);


#266 [start] +2251.4ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47), (@p48, @p49, @p50, @p51, @p52, @p53, @p54, @p55, @p56, @p57, @p58, @p59, @p60, @p61, @p62, @p63, @p64, @p65, @p66, @p67, @p68, @p69, @p70, @p71), (@p72, @p73, @p74, @p75, @p76, @p77, @p78, @p79, @p80, @p81, @p82, @p83, @p84, @p85, @p86, @p87, @p88, @p89, @p90, @p91, @p92, @p93, @p94, @p95)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#267 [start] +2251.8ms RuntimeElsaDbContext -- SaveChanges

#268 [start] +2251.8ms RuntimeElsaDbContext -- TransactionStarted

#269 [start] +2251.8ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#270 [start] +2251.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#271 [start] +2251.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#272 [start] +2251.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#273 [start] +2251.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#274 [start] +2251.9ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#275 [start] +2252.1ms RuntimeElsaDbContext -- TransactionCommitted

#276 [start] +2255.1ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '98a83d277dd23807')

#277 [start] +2255.2ms ManagementElsaDbContext -- SaveChanges

#278 [start] +2255.2ms ManagementElsaDbContext Reader
INSERT INTO "WorkflowInstances" ("Id", "CorrelationId", "CreatedAt", "Data", "DataCompressionAlgorithm", "DefinitionId", "DefinitionVersionId", "FinishedAt", "IncidentCount", "IsExecuting", "IsSystem", "Name", "ParentWorkflowInstanceId", "Status", "SubStatus", "TenantId", "UpdatedAt", "Version")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17);


#279 [start,BACKGROUND] +2257.3ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#280 [start,BACKGROUND] +2257.4ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#281 [start,BACKGROUND] +2760.0ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#282 [start,BACKGROUND] +2760.2ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#285 [resume] +4308.4ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."CorrelationId", "b"."CreatedAt", "b"."Hash", "b"."Name", "b"."SerializedMetadata", "b"."SerializedPayload", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Hash" = @filter_Hash AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId AND "b"."Name" = @filter_Name

#286 [resume] +4315.0ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."CorrelationId", "w"."CreatedAt", "w"."Data", "w"."DataCompressionAlgorithm", "w"."DefinitionId", "w"."DefinitionVersionId", "w"."FinishedAt", "w"."IncidentCount", "w"."IsExecuting", "w"."IsSystem", "w"."Name", "w"."ParentWorkflowInstanceId", "w"."Status", "w"."SubStatus", "w"."TenantId", "w"."UpdatedAt", "w"."Version"
FROM "WorkflowInstances" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @filter_Id

#287 [resume] +4316.2ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#288 [resume] +4328.5ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."CorrelationId", "b"."CreatedAt", "b"."Hash", "b"."Name", "b"."SerializedMetadata", "b"."SerializedPayload", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = @filter_BookmarkIds1 AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId

#289 [resume] +4332.5ms RuntimeElsaDbContext NonQuery
DELETE FROM "Bookmarks" AS "b"
WHERE (@ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)) AND "b"."Id" = @filter_BookmarkIds1 AND "b"."WorkflowInstanceId" = @filter_WorkflowInstanceId

#290 [resume] +4333.5ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47), (@p48, @p49, @p50, @p51, @p52, @p53, @p54, @p55, @p56, @p57, @p58, @p59, @p60, @p61, @p62, @p63, @p64, @p65, @p66, @p67, @p68, @p69, @p70, @p71)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#291 [resume] +4334.0ms RuntimeElsaDbContext -- SaveChanges

#292 [resume] +4334.1ms RuntimeElsaDbContext -- TransactionStarted

#293 [resume] +4334.1ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#294 [resume] +4334.1ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#295 [resume] +4334.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#296 [resume] +4334.2ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#297 [resume] +4334.4ms RuntimeElsaDbContext -- TransactionCommitted

#298 [resume] +4335.8ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = '98a83d277dd23807')

#299 [resume] +4335.9ms ManagementElsaDbContext -- SaveChanges

#300 [resume] +4335.9ms ManagementElsaDbContext Reader
UPDATE "WorkflowInstances" SET "CorrelationId" = @p0, "CreatedAt" = @p1, "Data" = @p2, "DataCompressionAlgorithm" = @p3, "DefinitionId" = @p4, "DefinitionVersionId" = @p5, "FinishedAt" = @p6, "IncidentCount" = @p7, "IsExecuting" = @p8, "IsSystem" = @p9, "Name" = @p10, "ParentWorkflowInstanceId" = @p11, "Status" = @p12, "SubStatus" = @p13, "TenantId" = @p14, "UpdatedAt" = @p15, "Version" = @p16
WHERE "Id" = @p17
RETURNING 1;


#301 [resume,BACKGROUND] +4337.2ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#302 [resume,BACKGROUND] +4337.4ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#303 [resume,BACKGROUND] +4838.5ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#304 [resume,BACKGROUND] +4839.0ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

