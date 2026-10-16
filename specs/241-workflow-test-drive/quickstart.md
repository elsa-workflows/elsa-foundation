# Validation journey

1. Start isolated rebuilt Workbench + Studio; create an unpublished multi-trigger workflow with declared customerId input.
2. Test Run → Event → configure payload; confirm selected branch, captured inputs and actual runtime outcome.
3. Test Run → HTTP → configure path/query/headers/body; prepare then send actual request. Confirm no production publication, real response and exact test execution.
4. Submit invalid input, stale option, denied policy, excessive body and wrong method; confirm no new execution.
5. Timer/Cron Fire once now; inspect UTC upcoming times and unchanged authored schedule.
6. Exercise supported active wait, duplicate delivery and Stop/expiry; verify isolation from global events/production.
7. Register a custom driver/editor in tests and prove it works without modifying built-ins.
8. Record the verified workflow in Screen Studio with narration and quiet original music after all relevant checks pass.
