# Zapper Payments Integration Service — SOLUTION

**Document status:** Working draft — Stage 3.4.1 verified (19 November 2026)

## 1. Overview

This solution implements a .NET 8 payment integration service for Zapper, acting as a payment facilitator for sub-merchants using Acme Pay. It resolves customer, stored card and merchant information, submits tokenised card payments, persists payment state and provides local status queries and settlement reporting. Acme is fictional; a separate local simulator supports development without paid cloud infrastructure.

The current baseline implements payment submission, sequential idempotency, lifecycle history, gateway diagnostics and simulator RTTQ. Application-side RTTQ reconciliation, notifications, automated coverage and containerisation remain outstanding.

## 2. Technology and architecture

- C# / .NET 8, ASP.NET Core Web API, Visual Studio 2022.
- SQL Server, EF Core 8 and migrations. Local development uses Windows authentication. SQL Server 2019 compatibility is intended; the observed local instance reports version 16.x (SQL Server 2022).
- Swagger and Postman for manual API validation; SSMS for persistence and reporting validation.
- Git for incremental checkpoints; xUnit projects established for subsequent automated testing.

A modular monolith separates responsibilities without introducing distributed business-service complexity:

```text
Zapper.Payments.Api
    -> Zapper.Payments.Application
    -> Zapper.Payments.Infrastructure
           -> Zapper.Payments.Application
           -> Zapper.Payments.Domain
Application -> Domain
Infrastructure -> SQL Server / Acme Pay HTTP endpoint
Zapper.AcmePay.Stub (independent process)
```

| Project | Responsibility |
|---|---|
| Domain | Entities and payment lifecycle enums |
| Application | Request/response DTOs, service interfaces, provider contracts and business exceptions |
| Infrastructure | EF Core persistence, HTTP integration, merchant descriptor formatting and payment orchestration |
| API | HTTP validation, controllers, dependency injection and status responses |
| AcmePay.Stub | Synthetic provider behaviour and RTTQ |
| UnitTests / IntegrationTests | Planned automated regression coverage |

The payment service currently resides in Infrastructure because it directly uses EF Core. Application owns the interfaces, keeping dependency direction correct. Provider-specific DTOs also live in Application; this is a pragmatic integration boundary rather than a fully provider-neutral abstraction. Repository abstractions and provider-neutral mapping could be introduced if additional providers or business complexity justify them.

## 3. Data model

| Table | Purpose |
|---|---|
| Customers | External customer identifier and email |
| StoredCards | Tokenised card reference, customer association and active flag |
| SubMerchants | Merchant identifier, trading name, city, region, country and MCC |
| Payments | Current state, app identifier, stable gateway references, requested/authorised/refunded amounts and timestamps |
| PaymentEvents | Lifecycle history including previous/new status, event amount and occurrence/recording timestamps |
| AcmeNotifications | Storage prepared for incoming notifications; processing not yet implemented |
| AcmeRequestLogs | Gateway request type, identifiers, HTTP and provider codes, duration, success flag and diagnostic errors |

Amounts are stored as integer minor units using 64-bit values, avoiding floating-point rounding. Currency is recorded separately. Payment state and event history are saved together using EF Core's normal atomic SaveChanges behaviour. Payment has a RowVersion concurrency token; actual concurrent update handling still requires verification and enhancement.

The intended database constraints include uniqueness of AppPaymentId and stable provider references. The service catches SQL Server duplicate-key errors 2601/2627 and reloads an existing app payment. Sequential duplicate handling has been validated; inspect migration constraints and test concurrent races before asserting concurrency guarantees.

The current twelve-character order number is generated from a GUID prefix. Collisions are possible; uniqueness constraints must catch them before submission. The service currently propagates unrelated uniqueness collisions rather than retrying with a new pre-submission reference. A sequence-backed or otherwise collision-resistant reference allocation strategy is a potential improvement.

As volume grows, add indexes based on measured query patterns (pending state, event dates, merchant and provider references), retention policies for diagnostics, and potentially partition large append-oriented event/notification tables. Do not remove audit history without settlement and retention requirements.

## 4. Payment processing

1. Validate the incoming request and look up AppPaymentId.
2. Return an existing payment if its customer, merchant, amount and currency match; reject conflicting reuse with HTTP 409.
3. Look up the customer, first active stored card (ordered by ID) and active sub-merchant. Selecting the first active card is an assessment assumption; a real multi-card application should supply an explicit card identifier.
4. Generate and preserve TransactionID and OrderNumber. Construct the provider request using the stored token and merchant details.
5. Save Pending payment and PaymentSubmitted event before making the external request.
6. Submit through the typed Acme HTTP client. Do not hold a database transaction open during network I/O.
7. Validate returned transaction and order references. Interpret provider result codes rather than relying solely on HTTP success.
8. Persist outcome, lifecycle event and request diagnostics. Use a separate bounded persistence cancellation token after submission so a client disconnect does not immediately prevent recording the outcome.

The save-before-send boundary preserves the original transaction identity in uncertain situations. A process crash can still leave a Pending record; recovery is planned. The existing implementation does not provide exactly-once processing across the database and external gateway.

### API surface

| Method and route | Behaviour |
|---|---|
| POST /api/payments | Create or return existing payment; 200 for resolved outcome, 202 for pending, 400 invalid input, 409 conflicting app identifier |
| GET /api/payments/{appPaymentId} | Retrieve local payment state; 200 or 404 |

Caller authentication is outside assessment scope. Provider callback authentication is required and remains to be implemented.

### State model

Pending (1), Approved (2), Declined (3), PendingReconciliation (4), Reversed (5), PartiallyRefunded (6), Refunded (7).

Current submission establishes Pending and transitions to Approved, Declined or PendingReconciliation. Reversal and refund states exist in the domain model but their transitions are not yet implemented.

A provider HTTP 200 with TransactionStatus Success does not necessarily mean the payment was approved. Code 00 with a valid positive authorised amount no greater than the requested amount is treated as approval. Codes 05, 51 and 54 from the acquirer are currently classified as declines. Other or uncertain outcomes are conservatively marked PendingReconciliation. Full classification against Appendix A remains to be reviewed. Partial authorisation is stored using AuthAmount; downstream business handling needs further validation.

## 5. Acme HTTP integration

A typed HttpClient registered through IHttpClientFactory manages HTTP connections. The client adds bearer authentication, serialises provider contracts with explicit PascalCase JSON property mappings, and propagates cancellation. The configured local timeout is 35 seconds. The current non-success HTTP handling throws HttpRequestException; structured provider errors and Retry-After handling remain to be improved. Raw error response bodies should not be included in exceptions or logs in the final version.

The provider request includes a unique message identifier, stable transaction identifier, order number, minor-unit amount, currency, stored token, CIT scenario and facilitator details. Provider timestamps are formatted in SAST (+02:00) without an offset and with millisecond precision. Internal record timestamps use UTC.

For FNB the merchant descriptor uses ZAP* followed by the merchant name, padded/truncated to 23 characters, city padded/truncated to 13, first two region characters and two country characters. Total length is 40; the asterisk is at position four. Validation of short/invalid region and country values and formatter unit tests remain pending.

Development configuration uses the AcmePay section in appsettings.Development.json. The local API calls http://localhost:5181/; local keys and merchant identifiers are synthetic. Final container and production configurations must use environment-supplied values and secrets rather than real credentials in committed files.

## 6. Acme simulator and RTTQ (Stage 3.4.1)

The independent ASP.NET Core simulator supports POST /api/payment and GET /apirttq/query/Merchant/{merchantId}. RTTQ accepts TransactionID or OrderNumber and uses the same bearer credentials. Unknown transactions return 404. The simulator checks the configured merchant account.

| Synthetic token | Behaviour |
|---|---|
| TEST_TOKEN_ALICE_001 / ordinary test tokens | Approved, code 00 |
| TEST_TOKEN_DECLINED | Declined, code 05 |
| TEST_TOKEN_INSUFFICIENT_FUNDS | Declined, code 51 |
| TEST_TOKEN_PROCESSING | Processing, code 91, empty AuthCode |
| TEST_TOKEN_TIMEOUT | Store approval first, then delay response by 40 seconds |

Transaction registration and retrieval-reference ownership are checked under a short synchronisation lock. No network delay occurs inside the lock. Duplicate transaction identifiers within 24 hours replay the original stored outcome. Different identifiers using an occupied retrieval reference return HTTP 409/E09 and the original owner's TransactionID. As a conservative stub-only decision, replay outside the 24-hour window is rejected rather than charging again.

The 40-second artificial delay intentionally exceeds the provider's documented normal maximum of 30 seconds to demonstrate response loss with the existing 35-second client timeout. The stub is single-account and in-memory: restarting it loses transaction history. Processing outcomes do not automatically resolve. Unknown synthetic tokens default to approval. Replaying/querying currently returns the original response envelope, including its original MessageID; fresh response message identifiers should be considered when tightening protocol fidelity. Rate limiting and gateway failure injection remain pending.

The real application must query the original transaction after timeout, never blindly create a different transaction ID. Stub RTTQ is verified, but the application's RTTQ client and automated reconciliation are not yet implemented.

## 7. Reporting

The SQL report at database/Reporting/DailyPaymentSummary.sql (intended repository location) uses lifecycle events to aggregate approvals, declines, reversals and refunds by merchant and currency. Approved totals use authorised amounts; declines use requested amounts. Event-based reporting avoids losing the original approval when a later refund changes current status.

The existing report has been manually validated for approval and decline counts and totals. Final verification must include a supplied reporting date, SAST business-day boundaries using UTC event timestamps, reversal/refund data and duplicate-notification handling. SAST business date is a proposed assumption to document and validate. Refund count is intended to represent individual successful refund events; reversal/refund totals must represent actual movements, not the original amount indiscriminately.

## 8. Validation evidence to date

Manual validation was performed by the developer using Visual Studio, Swagger, Postman and SSMS. The following is a record of reported results, not a claim of automated test execution.

### Payment API baseline

| Scenario | Observed/reported result |
|---|---|
| Approved APP-PAY-100001 | Payment ID 1, code 00, amount and authorised amount 1999 (R19.99), Approved |
| Declined APP-PAY-100002 | Payment ID 2, code 05, requested amount 25000 (R250.00), authorised amount 0, Declined |
| Same AppPaymentId submitted again | Reported passed; existing payment returned |
| Same AppPaymentId with changed amount | Reported passed; conflict |
| GET existing payment | HTTP 200, original approved payment returned |
| SQL persistence | 2 payments, 4 lifecycle events, 2 gateway logs |
| Gateway diagnostics | HTTP 200 for both, durations 131 ms and 33 ms; communication success distinct from financial approval |
| Report | Merchant ZAP0004182, ZAR: 1 approval / 1999; 1 decline / 25000; zero reversals/refunds |

These timings are local observations, not performance benchmarks.

### Simulator RTTQ verification (19 November 2026)

All nine checkpoint tests were reported successful: build, Swagger routes, approval, replay, query by transaction ID, query by order number, unknown transaction 404, duplicate reference E09 and timeout recovery via direct RTTQ.

Example observed submission / query pair:

```text
TransactionID: 76f73d62-060f-4569-a240-9263f4cc0001
OrderNumber / RRN: RTTQ00000001
ReconID: RECON64e8a9c39d
TransactionDateTime: 2026-11-19T19:53:38.347 (SAST)
TransactionStatus: Success
ResponseCode: 00
AuthAmount: 1999
AuthCode: 238699 (synthetic)
```

The supplied query response matches the original submission. It verifies provider-side lookup, not application-side recovery. Preserve test evidence in sanitised form under ai/ or a test evidence directory if useful.

## 9. AWS readiness — proposed design, not deployed

Run stateless API containers using ECS on Fargate behind an Application Load Balancer. Use ECR for images, RDS for SQL Server in private subnets, and Secrets Manager for database and Acme credentials. Use task IAM roles with least privilege, TLS and restricted networking. The fictional simulator should run only in local/test environments, not as a production payment provider.

Scale API instances using request rates, CPU and latency. Coordinate the provider's 20 requests/second account limit across all replicas; a per-instance limiter alone cannot enforce a global quota. Use a bounded concurrency limit, bounded queue and explicit overload handling. Respect Retry-After and use bounded backoff for eligible operations. Never automatically retry ambiguous payment submissions using a new transaction identifier.

A reconciliation worker should use database-backed ownership or equivalent coordination when multiple replicas are active, and optimistic concurrency to avoid overwriting final outcomes. On shutdown, stop accepting new work, propagate cancellation, allow bounded draining, persist uncertain outcomes and recover stale Pending records at startup/through reconciliation. Align ALB and container stop timeouts with the deliberate request budget. These are planned design choices, not yet validated implementation.

Use structured logs with payment and transaction correlation IDs, HTTP/provider result codes, duration, retry and reconciliation data. Never expose API keys, stored card tokens, callback secrets or sensitive payloads. Monitor gateway latency, 429/5xx rates, queue saturation, uncertain-payment age/count, webhook failures/duplicates, database errors and service health through CloudWatch or equivalent metrics and logs. Alarm on growing reconciliation backlog and sustained error rates. Separate business declines from infrastructure health.

Dockerfiles and a reproducible local container setup are still required by the assessment. Local Visual Studio execution is only the current development baseline. No paid AWS deployment is required.

## 10. AI-assisted development workflow

ChatGPT was used to interpret the assignment, discuss architecture, propose schema and code, generate simulator logic, construct manual tests, troubleshoot build errors and draft documentation. The developer supplied the assessment, chosen technology stack, actual entity definitions, error output, screenshots and API responses to ground the implementation.

Work was divided into architecture, database, provider contracts, simulator, HTTP client, payment persistence and reconciliation. Each stage was built and tested locally before proceeding. The developer created and integrated the projects, ran migrations, exercised endpoints, inspected SQL records and committed a verified baseline. The simulator was substantially AI-assisted, consistent with the assessment's emphasis on the integration service.

### Review and iteration examples

- Corrected dependency placement so Application owns the interface while Infrastructure owns HTTP implementation and EF-based orchestration.
- Resolved Swagger port mismatch by removing a hardcoded host binding and aligning configuration with launch profiles.
- Corrected AI-suggested Microsoft.Extensions package versions: 8.0.22 was unavailable for the named packages and NuGet resolved 9.0.0. Version 8.0.0 was recommended; confirm actual final package versions in the project file before submission.
- Diagnosed compilation failures consistent with PaymentService being in the wrong project; corrected placement was recommended. Verify the final physical location and references in the repository.
- Corrected the initial simulator's E09 response on same-transaction replay and the race between reference checks and insertion. Added a short atomic store lock and direct RTTQ tests.
- Validated that an HTTP 200/code 05 is a business decline rather than approval, and checked persisted authorised amounts and lifecycle events.

Do not present suggested corrections as independently reviewed source changes unless confirmed in the repository. AI-generated implementations were validated incrementally by the developer; the assistant did not execute the local .NET solution. Automated concurrency, notification, resilience and container testing still need completion.

### AI artifacts and improvements

Keep genuine prompts, architecture notes, review notes and sanitised session exports in ai/. In particular, preserve the Acme client development session if export is available. Distinguish original prompts from reconstructed task summaries. Do not commit personal information or real credentials. Suggested paths: ai/prompts/, ai/reviews/, ai/test-evidence/.

For a future iteration, establish contract and concurrency tests earlier, make provider outcome classification explicit, verify package versions before recommending installation, and define recovery semantics and global rate limits before implementing retries. Use automated tests as acceptance gates rather than relying only on manual happy-path tests.

## 11. Current delivery status

| Stage | Status |
|---|---|
| 1 — Solution architecture and project setup | Complete |
| 2 — SQL Server schema, EF migrations and seed data | Complete, manual baseline verified |
| 3.1 — Provider contracts and merchant formatter | Complete, edge-case tests pending |
| 3.2 — Local simulator and HTTP client | Complete, baseline verified |
| 3.3 — Payment processing, persistence and reporting | Complete for manual approval/decline baseline |
| 3.4.1 — Simulator replay and RTTQ | Complete, manual verification reported |
| 3.4.2 — Application RTTQ client | Next |
| 3.4.3 — Reconciliation service | Pending |
| 3.4.4 — Automated reconciliation worker | Pending |
| 3.4.5 — Outbound concurrency, rate limiting and resilience | Pending |
| 4 — Authenticated notifications, duplicates, out-of-order events, reversals and refunds | Pending |
| 5 — Automated unit and integration tests | Pending |
| 6 — Containerised local setup and AWS operability | Pending |
| 7 — Final README, SOLUTION.md, AI artifacts and 5–10 minute walkthrough | Pending |

## 12. Reference

Zapper Team Lead Software Engineer Technical Vetting Assignment, Tasks 1–4 and Appendix A (Acme Pay Payment Gateway API v2). This document is an implementation record and should be reconciled against the final source code and test results before submission.

## 13. Real Time Transaction Query (RTTQ) Client

The Acme client abstraction was extended to support Real Time Transaction Query using the original persisted TransactionID.

The RTTQ implementation uses the existing typed HttpClient, configured merchant identifier and bearer authentication.

A dedicated AcmeQueryResult contract separates HTTP transport outcomes from financial transaction outcomes. It preserves the HTTP status code, available provider response and Retry-After header.

HTTP 404 is treated as an unknown provider transaction rather than a confirmed financial decline. HTTP 429 is identified separately so that subsequent reconciliation scheduling can respect provider throttling.

Response downloading is included within the HTTP client's configured timeout, and sensitive provider response bodies are excluded from exception messages.

A temporary development-only diagnostic endpoint was introduced to validate communication between the Zapper Payments API and Acme RTTQ before implementing database reconciliation.

The timeout simulation demonstrates why a payment must be persisted before contacting an external gateway. A request can time out locally even though Acme has successfully authorised the original transaction.

The original TransactionID is retained and queried rather than generating a new identifier or submitting another financial transaction.

Automatic reconciliation, durable reconciliation scheduling, outbound rate limiting and lifecycle concurrency protection are addressed in subsequent implementation stages.

### 14. Payment Reconciliation Service

A dedicated payment reconciliation service resolves uncertain payment outcomes using the original persisted TransactionID.

Payments marked as PendingReconciliation are queried using Acme's Real Time Transaction Query endpoint without resubmitting the original financial transaction.

The reconciliation service validates the returned transaction identifier and order number before applying any financial state transition.

Confirmed approvals update the authorised amount, provider references, response code and payment status. Confirmed acquirer declines are recorded as declined payments. Unknown transactions, processing outcomes, communication failures and throttled requests remain unresolved.

A short SQL Server transaction uses update locks to coordinate concurrent reconciliation operations. The current payment state is rechecked before applying the provider outcome, preventing repeated reconciliation requests from generating duplicate financial lifecycle events.

Payment state changes, lifecycle events and reconciliation request diagnostics are persisted atomically.

Provider transaction timestamps are converted from South African Standard Time to UTC. Where the provider timestamp is unavailable or malformed, the observation time is used as a fallback.

A development-only manual reconciliation endpoint was introduced to validate timeout recovery, repeated reconciliation, unknown transactions and unresolved processing outcomes.

No database transaction is held open during the external HTTP request.

Automated reconciliation scheduling, stale Pending payment recovery, distributed outbound rate limiting and concurrency coordination with webhook processing remain subsequent implementation tasks.

## 15. Automated Background Payment Reconciliation

A hosted .NET BackgroundService automatically discovers payments requiring reconciliation and attempts to recover their original financial outcomes through Acme RTTQ.

The worker reuses the existing payment reconciliation service, ensuring that manual and automatic reconciliation follow the same transaction validation, concurrency protection, state transition and persistence rules.

Polling intervals, batch sizes, initial processing delays and retry intervals are configurable through application settings.

Dependency injection scopes are created for individual reconciliation operations to isolate EF Core DbContext instances and prevent scoped dependencies from being retained by the singleton hosted service.

Unresolved transactions are deferred until their next eligible retry time. Provider Retry-After instructions are respected by pausing further reconciliation requests from the worker.

Structured logging provides operational visibility into payment discovery, successful recovery, unresolved outcomes, provider throttling and processing failures.

Cancellation tokens and the hosted service lifecycle support graceful application shutdown.

The initial implementation processes reconciliation operations sequentially using bounded batches. Retry scheduling is maintained in memory, while payment state and reconciliation request history remain durable in SQL Server.

Durable retry scheduling, stale Pending payment recovery, distributed work claiming and shared outbound rate limiting remain production-hardening considerations.

### Stage 3.4.4 – Automated Reconciliation Validation

Automatic reconciliation was validated against the local Acme Pay simulator using controlled payment timeout and processing scenarios.

#### Timeout Recovery

- Application Payment ID: APP-AUTO-RECON-001
- Payment ID: 8
- Transaction ID: c9a5fb80-4898-4b10-aa66-bc2054cfc5ea
- Payment amount: 45000 minor units (ZAR 450.00)
- Original HTTP request duration: 35,267 milliseconds
- Automatic RTTQ request duration: 30 milliseconds
- Reconciliation completed approximately 11 seconds after the original timeout.
- Final payment status: Approved
- Final Acme response code: 00

Database validation confirmed:

- One original payment record.
- Three payment lifecycle events.
- One original payment submission request.
- One successful RTTQ request.
- The original TransactionID was preserved.
- No additional financial payment submission was recorded.

#### Unresolved Processing Scenario

A second payment, APP-AUTO-PROCESSING-001, was submitted using the simulator's processing token.

The application correctly returned HTTP 202 and persisted the payment as PendingReconciliation.

The background worker automatically queried Acme, received response code 91 and retained the unresolved payment status.

This validates that transport-level HTTP success is not incorrectly interpreted as financial approval.

#### Implementation Limitations

The current worker uses sequential processing with bounded batches and in-memory retry scheduling. Distributed work claiming, durable retry scheduling and gateway-wide rate limiting remain subsequent production-hardening tasks.

### Stage 3.4.5.1 – Shared Outbound Rate Limiting and Concurrency Control

A dedicated outbound resilience layer was introduced using a custom DelegatingHandler integrated with the existing typed Acme HTTP client.

Microsoft's System.Threading.RateLimiting library is used to provide request pacing and bounded concurrency.

A singleton AcmeRequestLimiter is shared across payment submission, transaction queries and background reconciliation requests within an application instance.

The initial configuration targets 18 requests per second, below Acme's documented maximum of 20 requests per second, with a maximum of 10 concurrent outbound operations.

A single-token bucket provides paced request admission rather than allowing large request bursts. A separate concurrency limiter restricts simultaneous HTTP operations and provides a bounded waiting queue.

Queue waiting is cancellation-aware and subject to a configurable five-second timeout.

Provider HTTP 429 responses establish a shared cooldown using the Retry-After response header or a configured fallback delay.

Financial payment submissions are not automatically retried by the HTTP handler. Existing payment reconciliation continues to use the original persisted TransactionID.

The resilience handler buffers the provider response before releasing its concurrency permit.

Existing payment submission, transaction querying and reconciliation business logic remain unchanged.

Configuration values are externally configurable and validated during application startup.

The current implementation provides application-instance-level rate limiting. Distributed gateway-wide rate limiting, explicit handling of requests rejected before dispatch, separate gateway timeout budgeting and concurrent load validation remain subsequent implementation tasks.

### Stage 3.4.5.1 – Functional Regression Validation

Following the introduction of the shared outbound resilience handler, regression testing was performed against the local Acme Pay simulator to ensure that existing payment processing and reconciliation functionality remained operational.

#### Approved Payment Scenario

- Application Payment ID: APP-RESILIENCE-001
- Payment ID: 10
- Amount: 6500 minor units (ZAR 65.00)
- HTTP status: 200
- Acme response code: 00
- Final payment status: Approved
- Authorised amount: 6500
- Recorded request duration: 77 milliseconds

#### Declined Payment Scenario

- Application Payment ID: APP-RESILIENCE-002
- Payment ID: 11
- Amount: 7500 minor units (ZAR 75.00)
- HTTP status: 200
- Acme response code: 05
- Final payment status: Declined
- Authorised amount: 0
- Recorded request duration: 21 milliseconds

The application continued to distinguish successful HTTP communication from successful financial authorisation.

#### RTTQ Regression

The diagnostic transaction query endpoint successfully retrieved the approved transaction using its original TransactionID.

The provider returned HTTP 200 with response code 00 and the expected authorised amount.

#### Automatic Timeout Recovery

- Application Payment ID: APP-RESILIENCE-TIMEOUT-001
- Payment ID: 12
- TransactionID: e9fa647d-13aa-4249-891b-6bf02c2a7076
- Amount: 8500 minor units (ZAR 85.00)
- Initial API response: HTTP 202
- Initial payment status: PendingReconciliation
- Original request duration: 35,269 milliseconds
- RTTQ request duration: 12 milliseconds
- Final payment status: Approved
- Final Acme response code: 00

SQL Server validation confirmed that the original transaction identifier was preserved and that the payment was recovered through RTTQ without an additional recorded financial submission.

#### Validation Summary

The functional regression tests passed for approved payments, declined payments, transaction querying, timeout handling, automatic reconciliation, database persistence and gateway diagnostics.

The configured rate limits, concurrent request limits, local queue saturation and provider cooldown behaviour still require dedicated load and failure-injection testing in subsequent stages.