# 0007 — Structured logging and RFC 7807 errors

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

Operators need to trace a failing request across logs and cloud platforms. Clients need a
machine-readable, consistent error shape. The project targets both AWS and Azure in its
documentation, so the logging format must be portable.

## Decision

- **Serilog** with `ReadFrom.Configuration` and request logging. In Development the console
  uses a readable template; outside Development every line is compact JSON. JSON lines are
  ingested natively by Azure Log Analytics / Application Insights and by AWS CloudWatch when
  shipped through a log agent.
- **Azure** — outside Development, when `Serilog:AzureMonitor:ConnectionString` is set, logs
  are also written directly to Application Insights (queryable from Azure Monitor / Log
  Analytics with KQL).
- **Correlation** — `CorrelationIdMiddleware` accepts or generates `X-Correlation-Id`, stores
  it on `HttpContext.Items`, echoes it on the response and pushes it into the Serilog context.
  Every problem response includes `traceId` and `correlationId`.
- **Errors** — `IExceptionHandler` maps domain/application exceptions to `problem+json` with a
  stable `code`, status-code pages give framework-generated 401/403/404/405/413/415 responses
  the same shape, and `ApiBehaviorOptions.InvalidModelStateResponseFactory` gives model-binding
  failures (malformed JSON, unparsable query values) the same `validation_failed` contract as
  FluentValidation. Stack traces appear only in Development.
- **Request logs use the final status** — Serilog request logging wraps exception handling, so
  handled 4xx responses are recorded as 4xx at Warning rather than an intermediate 500 at Error.
  The exception handler owns exception detail; 5xx responses log at Error. The logged request
  path omits the query string so search terms and other personal data are not persisted.
- **Browser headers** — `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` and
  `X-Frame-Options: DENY` are global; `/confirm-email` adds a focused CSP for its inline page.
- **CloudWatch** — no extra sink is required: the container writes JSON to stdout, which is
  the canonical CloudWatch Logs input. Structured properties are preserved as JSON keys and
  can be queried with CloudWatch Logs Insights.

## Consequences

- A single log line is enough to pivot from an alert to the request.
- Adding a sink (Seq, OpenSearch, Datadog) is configuration plus one package.
- Log volume in Development stays human-readable.
