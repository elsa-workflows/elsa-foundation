#261 [request] +2138.5ms RuntimeElsaDbContext Reader
SELECT "t"."Id", "t"."ActivityId", "t"."Hash", "t"."Name", "t"."SerializedPayload", "t"."TenantId", "t"."WorkflowDefinitionId", "t"."WorkflowDefinitionVersionId"
FROM "Triggers" AS "t"
WHERE (@ef_filter__p4 OR "t"."TenantId" = @ef_filter__TenantId OR "t"."TenantId" = '*' OR ("t"."TenantId" IS NULL AND @ef_filter__p3)) AND "t"."Hash" = @Hash

#262 [request] +2139.6ms ManagementElsaDbContext Reader
SELECT "w"."Id", "w"."BinaryData", "w"."CreatedAt", "w"."Data", "w"."DefinitionId", "w"."Description", "w"."IsLatest", "w"."IsPublished", "w"."IsReadonly", "w"."IsSystem", "w"."MaterializerContext", "w"."MaterializerName", "w"."Name", "w"."OriginalSource", "w"."ProviderName", "w"."StringData", "w"."TenantId", "w"."ToolVersion", "w"."UsableAsActivity", "w"."Version"
FROM "WorkflowDefinitions" AS "w"
WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = @id
ORDER BY "w"."CreatedAt"

#263 [request] +2155.1ms RuntimeElsaDbContext NonQuery
INSERT INTO "ActivityExecutionRecords" ("Id", "ActivityId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "AggregateFaultCount", "CallStackDepth", "CompletedAt", "HasBookmarks", "SchedulingActivityExecutionId", "SchedulingActivityId", "SchedulingWorkflowInstanceId", "SerializedActivityState", "SerializedActivityStateCompressionAlgorithm", "SerializedException", "SerializedMetadata", "SerializedOutputs", "SerializedPayload", "SerializedProperties", "StartedAt", "Status", "TenantId", "WorkflowInstanceId") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23), (@p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, @p34, @p35, @p36, @p37, @p38, @p39, @p40, @p41, @p42, @p43, @p44, @p45, @p46, @p47), (@p48, @p49, @p50, @p51, @p52, @p53, @p54, @p55, @p56, @p57, @p58, @p59, @p60, @p61, @p62, @p63, @p64, @p65, @p66, @p67, @p68, @p69, @p70, @p71), (@p72, @p73, @p74, @p75, @p76, @p77, @p78, @p79, @p80, @p81, @p82, @p83, @p84, @p85, @p86, @p87, @p88, @p89, @p90, @p91, @p92, @p93, @p94, @p95), (@p96, @p97, @p98, @p99, @p100, @p101, @p102, @p103, @p104, @p105, @p106, @p107, @p108, @p109, @p110, @p111, @p112, @p113, @p114, @p115, @p116, @p117, @p118, @p119)
ON CONFLICT("Id") DO UPDATE SET
"ActivityId"=excluded."ActivityId", "ActivityName"=excluded."ActivityName", "ActivityNodeId"=excluded."ActivityNodeId", "ActivityType"=excluded."ActivityType", "ActivityTypeVersion"=excluded."ActivityTypeVersion", "AggregateFaultCount"=excluded."AggregateFaultCount", "CallStackDepth"=excluded."CallStackDepth", "CompletedAt"=excluded."CompletedAt", "HasBookmarks"=excluded."HasBookmarks", "SchedulingActivityExecutionId"=excluded."SchedulingActivityExecutionId", "SchedulingActivityId"=excluded."SchedulingActivityId", "SchedulingWorkflowInstanceId"=excluded."SchedulingWorkflowInstanceId", "SerializedActivityState"=excluded."SerializedActivityState", "SerializedActivityStateCompressionAlgorithm"=excluded."SerializedActivityStateCompressionAlgorithm", "SerializedException"=excluded."SerializedException", "SerializedMetadata"=excluded."SerializedMetadata", "SerializedOutputs"=excluded."SerializedOutputs", "SerializedPayload"=excluded."SerializedPayload", "SerializedProperties"=excluded."SerializedProperties", "StartedAt"=excluded."StartedAt", "Status"=excluded."Status", "TenantId"=excluded."TenantId", "WorkflowInstanceId"=excluded."WorkflowInstanceId";


#264 [request] +2156.3ms RuntimeElsaDbContext -- SaveChanges

#265 [request] +2156.4ms RuntimeElsaDbContext -- TransactionStarted

#266 [request] +2156.4ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#267 [request] +2156.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#268 [request] +2156.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#269 [request] +2156.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#270 [request] +2156.5ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#271 [request] +2156.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#272 [request] +2156.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#273 [request] +2156.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#274 [request] +2156.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#275 [request] +2156.6ms RuntimeElsaDbContext Reader
INSERT INTO "WorkflowExecutionLogRecords" ("Id", "ActivityId", "ActivityInstanceId", "ActivityName", "ActivityNodeId", "ActivityType", "ActivityTypeVersion", "EventName", "Message", "ParentActivityInstanceId", "Sequence", "SerializedActivityState", "SerializedPayload", "Source", "TenantId", "Timestamp", "WorkflowDefinitionId", "WorkflowDefinitionVersionId", "WorkflowInstanceId", "WorkflowVersion")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19);


#276 [request] +2156.8ms RuntimeElsaDbContext -- TransactionCommitted

#277 [request] +2160.1ms ManagementElsaDbContext Reader
SELECT EXISTS (
    SELECT 1
    FROM "WorkflowInstances" AS "w"
    WHERE (@ef_filter__p4 OR "w"."TenantId" = @ef_filter__TenantId OR "w"."TenantId" = '*' OR ("w"."TenantId" IS NULL AND @ef_filter__p3)) AND "w"."Id" = 'b7c546e3b7a95282')

#278 [request] +2160.2ms ManagementElsaDbContext -- SaveChanges

#279 [request] +2160.3ms ManagementElsaDbContext Reader
INSERT INTO "WorkflowInstances" ("Id", "CorrelationId", "CreatedAt", "Data", "DataCompressionAlgorithm", "DefinitionId", "DefinitionVersionId", "FinishedAt", "IncidentCount", "IsExecuting", "IsSystem", "Name", "ParentWorkflowInstanceId", "Status", "SubStatus", "TenantId", "UpdatedAt", "Version")
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17);


#280 [request,BACKGROUND] +2163.0ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#281 [request,BACKGROUND] +2163.2ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

#282 [request,BACKGROUND] +2666.0ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)

#283 [request,BACKGROUND] +2666.1ms RuntimeElsaDbContext Reader
SELECT "b"."Id", "b"."ActivityInstanceId", "b"."ActivityTypeName", "b"."BookmarkId", "b"."CorrelationId", "b"."CreatedAt", "b"."DeliveryAttempts", "b"."LastAttemptedAt", "b"."LastErrorMessage", "b"."LastErrorType", "b"."SerializedOptions", "b"."StimulusHash", "b"."TenantId", "b"."WorkflowInstanceId"
FROM "BookmarkQueueItems" AS "b"
WHERE @ef_filter__p4 OR "b"."TenantId" = @ef_filter__TenantId OR "b"."TenantId" = '*' OR ("b"."TenantId" IS NULL AND @ef_filter__p3)
ORDER BY "b"."CreatedAt"
LIMIT @p1 OFFSET @p

