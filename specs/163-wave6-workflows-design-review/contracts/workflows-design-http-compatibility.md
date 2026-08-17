# Workflows Design HTTP Compatibility Contract

For every one of the 27 existing routes, the before and after observations must agree on:

1. route and method;
2. route/query/header/body binding and precedence;
3. status, JSON body, response headers, and content type;
4. ProblemDetails/domain error details, including 404, 409, 501, and 500 cases; and
5. pagination/filter values and concurrency/conflict behavior.

The evidence corpus must contain successful and failure cases, while the comparer must reject unused,
one-sided, blanket, or fixture-mutation approvals.
