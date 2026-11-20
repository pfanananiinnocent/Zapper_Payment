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

### Stage 3.4.5.2 – HTTP Resilience and Safe Failure Classification

The outbound resilience implementation was extended to distinguish requests rejected before dispatch from requests with uncertain provider outcomes.

A dedicated AcmeRequestNotDispatchedException identifies local queue saturation and admission timeouts before an HTTP request reaches the payment provider.

A NotDispatched payment status and corresponding lifecycle event were introduced. Payments in this state are not classified as financial declines and are excluded from automatic RTTQ reconciliation.

The Payments API returns HTTP 503 for payments rejected before dispatch. Existing application payment identity and idempotency protections are preserved.

Independent timeout budgets were introduced for outbound queue admission and provider execution. The initial configuration permits five seconds for queue admission and 35 seconds for provider communication.

SocketsHttpHandler was configured with explicit connection establishment timeouts, pooled connection lifetimes, idle connection timeouts and connection limits.

Automatic HTTP redirects and cookie handling were disabled for the Acme client.

The local Acme simulator was extended with deterministic HTTP 429 and HTTP 500 fault scenarios.

The HTTP 500 scenario intentionally processes and stores the financial transaction before returning an error, allowing recovery through RTTQ using the original TransactionID.

The provider Retry-After header establishes a shared application-instance cooldown. Requests unable to obtain admission within the configured queue timeout are rejected locally without contacting Acme.

No automatic retry of financial POST requests was introduced.

The design deliberately prioritises financial correctness over automatic recovery of requests known never to have been dispatched.

Remaining limitations include durable retry scheduling, stale Pending payment recovery, distributed rate limiting, explicit retry workflows for NotDispatched payments and finite response-body size enforcement.

Implementation validation is pending execution of the dedicated fault-injection and regression tests.

### Stage 3.4.5.2 – HTTP Resilience Validation and Test Evidence

**Validation date:** 19 November 2026

Dedicated fault-injection tests were executed using the local Acme Pay simulator, Swagger and SQL Server to validate the outbound resilience implementation.

#### HTTP 500 Recovery

- Application Payment ID: APP-HTTP500-001
- Payment ID: 13
- Amount: 9500 minor units (ZAR 95.00)
- Provider HTTP response: 500
- Initial API response: HTTP 202
- Initial status: PendingReconciliation
- Original request duration: 476 milliseconds
- RTTQ response: HTTP 200
- RTTQ duration: 28 milliseconds
- Final payment status: Approved
- Final Acme response code: 00
- Authorised amount: 9500

The simulator processed the financial transaction before returning an HTTP 500 error. The background reconciliation worker successfully recovered the original transaction using RTTQ without an additional recorded financial submission.

#### Provider HTTP 429 Handling

- Application Payment ID: APP-HTTP429-001
- Payment ID: 14
- Amount: 10500 minor units (ZAR 105.00)
- Provider HTTP status: 429
- API response: HTTP 202
- Payment status: PendingReconciliation

Subsequent RTTQ requests returned HTTP 404 because the simulated provider rejected the transaction before processing it.

The payment remained unresolved under the conservative reconciliation policy.

#### Retry-After Validation

The RTTQ diagnostic endpoint successfully extracted the following provider response properties:

- HTTP status: 429
- IsSuccessful: false
- IsRateLimited: true
- RetryAfterSeconds: 10
- Acme response code: E29

#### Shared Provider Cooldown Validation

A controlled test was executed with the simulator temporarily returning a Retry-After value of 120 seconds.

Application Payment ID: APP-HTTP429-002

- Payment ID: 17
- Amount: 13500 minor units (ZAR 135.00)
- Provider HTTP status: 429
- Initial API response: HTTP 202
- Payment status: PendingReconciliation
- Original payment request duration: 231 milliseconds

The background reconciliation worker attempted an RTTQ request during the active provider cooldown.

The shared outbound limiter rejected the RTTQ request before dispatch after approximately 5246 milliseconds.

This confirmed that payment submissions and background reconciliation requests share the same provider cooldown within the application instance.

#### Local NotDispatched Validation

- Application Payment ID: APP-NOT-DISPATCHED-002
- Payment ID: 18
- Amount: 14500 minor units (ZAR 145.00)
- API response: HTTP 503
- Final status: NotDispatched
- Database status value: 8
- Previous status: Pending (1)
- Lifecycle event type: PaymentNotDispatched (8)
- Authorised amount: NULL
- Acme response code: NULL
- Gateway HTTP status: NULL
- IsSuccessful: false
- Local admission timeout duration: 5174 milliseconds

The payment request was rejected by the shared outbound limiter before being dispatched to Acme.

SQL Server validation confirmed the correct payment status, lifecycle event and request diagnostics. No RTTQ requests were observed for the rejected payment.

#### Cooldown Recovery

- Application Payment ID: APP-COOLDOWN-RECOVERY-001
- Payment ID: 16
- Amount: 12500 minor units (ZAR 125.00)
- Final payment status: Approved
- Acme response code: 00
- Authorised amount: 12500
- Request duration: 74 milliseconds

Normal payment processing resumed after the provider cooldown expired.

#### Engineering Decisions

- Financial POST requests are not automatically retried.
- Uncertain financial outcomes are reconciled using the original TransactionID.
- Requests rejected before dispatch are not classified as financial declines.
- NotDispatched payments are excluded from automatic RTTQ reconciliation.
- Provider cooldown is shared across outbound Acme operations within one application instance.
- Queue admission and gateway execution have independent timeout budgets.

#### Validation Summary

All five primary functional resilience scenarios passed.

Remaining verification includes repeated NotDispatched idempotency, concurrent throughput enforcement, maximum in-flight request limits, sustained queue saturation, restart recovery and distributed coordination.

The provider cooldown test configuration must be restored to its normal value before subsequent testing.

#### Final Database Idempotency Verification

Following the repeated submission of APP-NOT-DISPATCHED-002 after the provider cooldown expired, SQL Server verification confirmed:

- Payment status: NotDispatched (8)
- Total outbound request log records: 1
- Original payment identity preserved
- No additional outbound request recorded
- No unintended payment status transition

The API returned the original HTTP 503 response and preserved the existing PaymentID, TransactionID and OrderNumber.

This validates both API-level and database-level idempotency for locally rejected payment requests under the tested scenario.

**Stage 3.4.5.2 validation status: Completed — all ten functional validation checks passed.**

Concurrent load testing and multi-instance coordination remain separate validation activities.

#### Per-Run Database Integrity Verification

Additional SQL Server validation was completed for all four concurrent load-test runs.

| Validation | Expected | Actual | Result |
|---|---:|---:|---|
| Total persisted payments | 180 | 180 | Passed |
| Unique AppPaymentIds across individual runs | 180 | 180 | Passed |
| Unique TransactionIds across individual runs | 180 | 180 | Passed |
| Unique OrderNumbers across individual runs | 180 | 180 | Passed |
| Approved payments | 180 | 180 | Passed |
| Total requested amount | 270000 | 270000 | Passed |
| Total authorised amount | 270000 | 270000 | Passed |
| Financial discrepancy | 0 | 0 | Passed |

All four concurrent test runs individually maintained identifier uniqueness and financial consistency.

No duplicate identifiers were detected within any of the individual test runs. All submitted payments were successfully authorised and persisted.

The baseline tests demonstrate that the payment solution maintains transactional and financial correctness under the tested concurrent workloads of up to 100 simultaneous client requests.

Provider-side rate-limit enforcement, maximum active outbound concurrency and concurrent submissions using the same AppPaymentId require separate validation.

**Stage 3.4.5.3 Part A — Completed successfully.**

### Stage 3.4.5.3B – Provider-Side Concurrency and Rate-Limit Instrumentation

#### Objective

Independently validate the effectiveness of the Payments API's outbound concurrency management, rate limiting and queue management by measuring actual incoming traffic at the Acme Pay simulator.

#### Implementation

Provider-side instrumentation was implemented using ASP.NET Core middleware and a thread-safe singleton metrics service.

The instrumentation monitors Payment and Real-Time Transaction Query (RTTQ) requests through shared counters and a rolling one-second request arrival window.

The implementation captures:

- Total Payment and RTTQ requests.
- Current and peak provider concurrency.
- Current and peak rolling request arrival rates.
- Concurrency and rate-limit threshold violations.
- Completed and aborted requests.
- HTTP response classifications.
- Average and maximum provider processing durations.

A monotonic Stopwatch clock is used for duration measurements and rolling-window calculations.

The instrumentation observes traffic independently without enforcing the Payments API's outbound limits.

#### Diagnostic Endpoints

- GET /api/diagnostics/metrics
- POST /api/diagnostics/metrics/reset

Both diagnostic endpoints are restricted to the development environment. Metrics reset is prevented while tracked requests remain active.

#### Configured Thresholds

- Maximum outbound concurrency: 10.
- Maximum request arrival rate: 20 requests per rolling second.
- Configurable Payment and RTTQ processing delays.

#### Validation Test 1 – Metrics Initialisation

Result: Passed.

The diagnostics endpoint returned HTTP 200. All counters were correctly initialised to zero, and the configured concurrency and rate-limit thresholds were loaded successfully.

#### Validation Test 2 – Concurrent Baseline

The Payments API received 100 concurrent payment submissions without an artificial provider delay.

Provider-side results:

| Metric | Observed result |
|---|---:|
| Total provider requests | 105 |
| Payment requests | 100 |
| RTTQ requests | 5 |
| Completed requests | 105 |
| Peak concurrent requests | 2 |
| Peak rolling requests per second | 19 |
| Rate-limit violations | 0 |
| Concurrency violations | 0 |
| HTTP 2xx responses | 100 |
| HTTP 4xx responses | 5 |
| HTTP 429 responses | 0 |
| HTTP 5xx responses | 0 |
| Aborted requests | 0 |
| Average provider duration | 2.33 ms |
| Maximum provider duration | 101.59 ms |

The configured outbound thresholds were not exceeded.

Five additional RTTQ requests were observed during the test window, likely originating from background reconciliation of previously unresolved payments.

The test therefore provides preliminary mixed-traffic validation rather than a strictly isolated payment-only baseline. The aggregate metrics do not independently establish the request types associated with the five HTTP 4xx responses.

#### Validation Test 3 – Controlled Provider Saturation

Run ID: LOAD-20261119-234429-7b9c6bd1

The simulator was configured with an artificial payment processing delay of 750 milliseconds.

A total of 50 concurrent payment submissions were executed.

Client-side results:

| Metric | Observed result |
|---|---:|
| Total requests | 50 |
| HTTP 200 responses | 50 |
| Approved payments | 50 |
| Unique payment IDs | 50 |
| Unique transaction IDs | 50 |
| Unexpected results | 0 |
| Average latency | 7,725.30 ms |
| P95 latency | 9,141 ms |
| Maximum latency | 9,619 ms |
| Total execution duration | 10.328 seconds |
| Client throughput | 4.84 requests/second |

Provider-side results:

| Metric | Observed result |
|---|---:|
| Total requests | 50 |
| Payment requests | 50 |
| RTTQ requests | 0 |
| Completed requests | 50 |
| Peak concurrent requests | 10 |
| Peak rolling requests per second | 14 |
| Rate-limit violations | 0 |
| Concurrency violations | 0 |
| HTTP 2xx responses | 50 |
| HTTP 429 responses | 0 |
| HTTP 5xx responses | 0 |
| Aborted requests | 0 |
| Average provider duration | 772.56 ms |
| Maximum provider duration | 1,221.42 ms |

The provider reached the configured concurrency limit of 10 without exceeding it.

All 50 payments were approved successfully, with unique payment and transaction identifiers and no unexpected client errors.

#### Engineering Conclusion

The observed results demonstrate that the outbound resilience controls enforce the configured concurrency and rate thresholds under the tested workloads.

The controlled saturation test also demonstrates the trade-off between protecting downstream services and increasing end-to-end request latency.

#### Design Limitations

- Metrics are in-memory and specific to the running simulator instance.
- Local validation does not establish distributed rate-limit compliance across multiple Payments API instances.
- The rolling one-second measurement is a conservative interpretation of the provider's rate restriction.
- End-to-end request latency includes multiple processing stages and cannot be attributed exclusively to outbound queue waiting.
- Provider-side diagnostics are intended for assessment testing and are not a replacement for production distributed monitoring.

#### Validation Status

- Provider-side instrumentation: Passed.
- Metrics initialisation and retrieval: Passed.
- Metrics reset: Executed; independently verify reset before the negative-control test.
- Baseline rate-limit compliance: Passed.
- Controlled provider saturation: Passed.
- Concurrent payment and RTTQ traffic: Preliminary validation.
- Instrumentation negative-control testing: Pending.
- Dedicated mixed-traffic saturation testing: Pending.
- Concurrent idempotency testing: Next stage.

#### Validation Test 4 – Instrumentation Negative Control

Result: Passed.

**Objective**

Validate that the provider-side instrumentation independently detects concurrency and rate-limit violations when the Payments API's outbound resilience controls are bypassed.

**Test Method**

- Reset the Acme simulator metrics.
- Configure an artificial payment processing delay of 750 milliseconds.
- Submit 30 concurrent HTTP requests directly to the Acme Pay simulator.
- Intentionally provide invalid authentication credentials to prevent successful simulated financial transactions.
- Retrieve the provider-side diagnostic metrics.

**Observed Results**

| Metric | Observed result |
|---|---:|
| Total provider requests | 31 |
| Payment requests | 30 |
| RTTQ requests | 1 |
| Completed requests | 31 |
| Peak concurrent requests | 29 |
| Expected maximum concurrency | 10 |
| Peak rolling requests per second | 30 |
| Configured requests per second limit | 20 |
| Rate-limit violations | 10 |
| Concurrency-limit violations | 20 |
| Rate-limit compliant | False |
| Concurrency-limit compliant | False |
| HTTP 4xx responses | 31 |
| HTTP 5xx responses | 0 |
| Aborted requests | 0 |
| Active requests after completion | 0 |
| Average provider duration | 763.21 ms |
| Maximum provider duration | 957.67 ms |

All 30 direct payment requests returned HTTP 401 as expected.

One additional RTTQ request was observed, potentially originating from background reconciliation. The test was therefore not completely isolated, but both configured threshold violations were successfully detected.

**Engineering Conclusion**

The negative-control test confirms that the provider instrumentation can independently detect violations rather than merely report successful compliance.

The protected load tests remained within the configured thresholds, while direct unprotected traffic exceeded both thresholds.

This establishes confidence in the effectiveness of the outbound resilience controls under the tested single-instance workloads.

Distributed enforcement across multiple application instances remains a production architecture consideration.

#### Validation Test 5 – Mixed Payment and RTTQ Traffic

**Objective**

Validate outbound resilience controls when the system receives both payment submissions and transaction reconciliation queries under concurrent workloads.

**Test Configuration**

- Maximum outbound concurrency: 10.
- Maximum outbound request rate: 20 requests per second.
- Simulated payment processing delay: 750 milliseconds.
- Simulated RTTQ processing delay: 750 milliseconds.
- Payment submissions: 50.
- Requested payment concurrency: 50.
- RTTQ diagnostic submissions: 50.
- Requested RTTQ concurrency: 50.

Payment load test run ID: `LOAD-20261119-235342-08ab0a14`.

**Payment API Results**

| Metric | Result |
|---|---:|
| Total payment submissions | 50 |
| Completed results | 50 |
| Approved payments | 50 |
| Unique payment IDs | 50 |
| Unique transaction IDs | 50 |
| HTTP 200 responses | 50 |
| Unexpected results | 0 |
| Minimum latency | 1499 ms |
| Average latency | 5780.20 ms |
| P95 latency | 10442 ms |
| Maximum latency | 10588 ms |
| Total execution time | 12.362 seconds |
| Payment API throughput | 4.04 requests/sec |

**Cumulative Provider Metrics**

| Metric | Result |
|---|---:|
| Total requests | 215 |
| Payment requests | 150 |
| RTTQ requests | 65 |
| Completed requests | 215 |
| Peak concurrent requests | 10 |
| Peak rolling requests per second | 14 |
| Rate-limit violations | 0 |
| Concurrency-limit violations | 0 |
| HTTP 429 responses | 0 |
| HTTP 5xx responses | 0 |
| Aborted requests | 0 |
| Active requests after completion | 0 |
| Average provider request duration | 764.31 ms |
| Maximum provider request duration | 887.73 ms |
| Rate-limit compliant | True |
| Concurrency-limit compliant | True |

**Test Observations and Limitations**

All 50 dedicated RTTQ diagnostic requests returned HTTP 404 because the queried transaction was unavailable in the simulator. This is consistent with the simulator's in-memory transaction storage following a restart.

These requests remain useful for outbound traffic validation, but do not demonstrate successful RTTQ retrieval under concurrent load.

The provider metrics accumulated additional requests beyond the dedicated 100-request workload. Consequently, the results demonstrate cumulative compliance rather than an isolated mixed-traffic benchmark.

**Engineering Conclusion**

The tested workloads remained within both configured outbound thresholds. Payment submissions completed successfully, provider concurrency reached the configured maximum of 10, and the rolling request rate remained below 20 requests per second.

The solution trades additional request latency for controlled provider utilisation and downstream protection.

These results provide supporting evidence for shared outbound resilience controls in the tested single-instance deployment. Distributed rate limiting and concurrency coordination across horizontally scaled instances remain production architecture considerations.

#### Stage 3.4.5.3B – Final Validation Summary

The following scenarios were successfully exercised:

- Provider-side diagnostic instrumentation.
- Baseline concurrent payment load.
- Slow-provider concurrency saturation.
- Independent negative-control violation detection.
- Payment and RTTQ traffic validation.
- Provider request-rate monitoring.
- Provider concurrency monitoring.
- Request completion and latency monitoring.

The instrumentation independently demonstrated threshold violations when bypassing the Payments API and compliant behaviour when using the protected application endpoints.

Validation scope: local, single-instance deployment with cumulative mixed-traffic metrics. No production-scale performance guarantees are inferred from these results.

#### Database Financial Integrity and Event Verification

Database reconciliation was performed against the three independent concurrent duplicate-payment test runs.

| Metric | 10 requests | 50 requests | 100 requests |
|---|---:|---:|---:|
| PaymentId | 499 | 509 | 518 |
| Persisted payment records | 1 | 1 | 1 |
| Unique application payment IDs | 1 | 1 | 1 |
| Unique transaction IDs | 1 | 1 | 1 |
| Unique order numbers | 1 | 1 | 1 |
| Approved payments | 1 | 1 | 1 |
| Requested minor units | 1500 | 1500 | 1500 |
| Authorised minor units | 1500 | 1500 | 1500 |
| Registration events | 1 | 1 | 1 |
| Approval events | 1 | 1 | 1 |
| Outbound payment request logs | 1 | 1 | 1 |
| Validation result | PASSED | PASSED | PASSED |

#### Consolidated Financial Results

- Total client submissions: 160.
- Actual persisted payments: 3.
- Unique financial transaction identifiers: 3.
- Actual outbound provider submissions: 3.
- Duplicate outbound submissions prevented: 157.
- Total requested minor units: 4500.
- Total authorised minor units: 4500.
- Total authorised value: ZAR 45.00.
- Duplicate approval events: 0.
- Financial integrity validation: PASSED.

Assuming every unprotected submission would have been independently approved, the potential authorised value would have been ZAR 2400.00. The implemented idempotency protection reduced the observed financial submissions to three, preventing potential duplicate authorisations totalling ZAR 2355.00.

#### Payment Event Integrity

Each payment generated exactly one registration event and one approval event.

All three payments transitioned from Pending to Approved without duplicate financial approval events.

This preserves the integrity of event-based financial reporting and prevents repeated application requests from inflating approval counts or authorised totals.

#### Outbound Request Log Verification

SQL Server request logs confirmed exactly one successful Acme payment submission per independent test run.

All three provider responses returned HTTP 200 with response code 00.

The observed outbound request durations were 1664 ms, 1592 ms and 1560 ms.

These results were consistent with the independent Acme provider instrumentation.

#### Conflicting Payment Payload Verification

PaymentId: 522

AppPaymentId: APP-IDEMPOTENCY-CONFLICT-001

The observed conflicting request returned HTTP 409 Conflict.

Subsequent database verification confirmed:

- Original requested amount: 2500 minor units.
- Original authorised amount: 2500 minor units.
- Final payment status: Approved.
- Outbound financial submissions: 1.
- Financial approval events: 1.

The conflicting request did not modify the original financial transaction or initiate another provider payment submission.

#### Engineering Conclusion

Concurrent idempotency was successfully validated through API response identities, database uniqueness, payment lifecycle events, outbound request logs and independent provider instrumentation.

All three duplicate load scenarios maintained financial consistency under the tested single-instance workload.

No core payment orchestration changes were required following these tests.

The results demonstrate effective duplicate financial submission protection for the tested scenarios, rather than a universal exactly-once guarantee across distributed systems.

Remaining validation activities include confirming all conflicting payload variations and correcting the load-testing utility's classification of legitimate HTTP 202 Pending responses.

#### Load-Testing Utility Regression Validation

The enhanced concurrent payment load-testing utility was successfully validated using both Duplicate and Unique execution modes.

##### Duplicate Mode Regression

Run ID: LOAD-20261120-001708-3683d68d

Test configuration:

- Total requests: 100.
- Requested concurrency: 100.
- Simulated provider processing delay: 1500 milliseconds.
- Payment amount: 1500 minor units.

Observed results:

| Metric | Result |
|---|---:|
| Completed responses | 100 |
| HTTP 200 Approved | 88 |
| HTTP 202 Pending | 12 |
| Valid responses | 100 |
| Invalid responses | 0 |
| Unexpected responses | 0 |
| Client errors | 0 |
| Unique application payment IDs | 1 |
| Unique payment IDs | 1 |
| Unique transaction IDs | 1 |
| Unique order numbers | 1 |
| Provider payment submissions | 1 |
| Provider successful responses | 1 |
| Provider rate-limit violations | 0 |
| Provider concurrency violations | 0 |
| Identity validation | PASSED |
| Overall API validation | PASSED |

The enhanced script correctly recognised HTTP 202 Pending responses as legitimate intermediate outcomes during concurrent duplicate processing.

All 100 responses referenced the same payment identity, transaction identity and order number.

Independent provider instrumentation confirmed that exactly one financial payment request reached the Acme simulator.

##### Unique Mode Regression

Run ID: LOAD-20261120-001829-884aeec3

| Metric | Result |
|---|---:|
| Total requests | 10 |
| Requested concurrency | 10 |
| Completed responses | 10 |
| Approved responses | 10 |
| HTTP 200 responses | 10 |
| Valid responses | 10 |
| Invalid responses | 0 |
| Unexpected responses | 0 |
| Client errors | 0 |
| Unique payment IDs | 10 |
| Unique transaction IDs | 10 |
| Unique order numbers | 10 |
| Average API latency | 1252.10 ms |
| P95 API latency | 1352 ms |
| Total test duration | 1.527 seconds |
| API throughput | 6.55 requests per second |
| Overall API validation | PASSED |

The Unique mode regression confirmed that the enhanced response validation did not introduce an observed regression into independent payment processing.

##### Consolidated Concurrent Idempotency Results

Across four independent duplicate-mode load tests:

- Total client submissions: 260.
- Actual outbound provider payment submissions: 4.
- Duplicate outbound payment submissions prevented: 256.
- Reduction in financial submissions: 98.46%.
- Observed duplicate provider submissions: 0.

##### Engineering Conclusion

The concurrent payment testing utility successfully validates response contracts, payment identity consistency and mode-specific acceptance criteria.

The original Pending response classification issue was resolved without modifying the core payment orchestration implementation.

The combination of database-level unique constraints, application-level idempotency, stable provider transaction identifiers, payment event auditing and independent provider instrumentation demonstrated effective duplicate financial submission protection under the tested single-instance workloads.

Distributed failure scenarios and multi-instance deployment considerations remain separate architectural concerns requiring additional production safeguards.

### Stage 4.1 — Acme Payment Advice Contracts and Validation

Implemented the Acme Payment Advice notification contract based on the assessment specification.

The notification contract is separate from the synchronous payment response envelope and preserves the provider's original JSON field names through explicit serialization attributes.

Implementation includes:

- Dedicated Payment Advice notification DTO.
- Required and optional field validation.
- Provider-defined field length validation.
- Merchant and transaction GUID validation for application-generated identifiers.
- Integer minor-unit financial amount validation.
- Default ZAR currency handling.
- Supported notification request type validation.
- Communication protocol validation.
- Explicit SAST-to-UTC timestamp conversion.
- Forward compatibility with unknown JSON fields.
- Separation of structural validation from financial business rules.

Payment outcome interpretation remains the responsibility of the notification processing layer. A structurally valid notification does not automatically represent an approved payment.

Unit tests cover valid and invalid contracts, missing fields, malformed identifiers, currency validation, timezone conversion, unsupported request types and forward-compatible deserialization.

Reversals and refunds will be handled separately through the Transaction Status webhook.

No database schema changes were required during this stage.

The notification endpoint will be introduced together with authentication and durable processing to prevent premature acknowledgement of financial notifications.

### Stage 4.3 — Payment Advice Notification Processing

Implemented authenticated Acme Payment Advice notification processing using the existing Payments, PaymentEvents and AcmeNotifications tables.

Notifications are matched to existing payments using the original Acme TransactionID. Merchant, amount, currency and payment references are validated before financial state changes are applied.

A deterministic SHA256 fingerprint prevents repeated processing of equivalent notifications.

Payment updates, lifecycle events and notification records are persisted within a single database transaction. Existing SQL Server row locking protects against concurrent notification processing.

Late or conflicting Payment Advice notifications are recorded without overwriting an already resolved payment.

Duplicate notifications receive HTTP 200 acknowledgement without generating additional financial events.

#### Assessment Assumptions

- Only Payment requests are submitted by this service.
- Approved Payment Advice notifications are assumed to represent full authorisation.
- Confirmed payment outcomes are not overwritten by subsequent conflicting Payment Advice notifications.
- Reversal and refund transitions are handled separately through the Transaction Status webhook.
- Unknown transactions and mismatched payment details are rejected and logged.
- Notification processing is synchronous for simplicity.

The implementation reuses the existing data model without introducing additional infrastructure or architectural layers.

## Stage 4.4 — Transaction Status Webhooks, Reversals and Refunds

### Implementation

Implemented the Acme Transaction Status webhook according to Appendix A of the assessment specification.

- Added the `/api/notifications/acme/transaction-status` endpoint.
- Implemented HMAC-SHA256 signature validation against the original request body.
- Externalised the webhook secret through `ACME_WEBHOOK_SECRET`.
- Matched incoming notifications to original payments using the retrieval reference number.
- Implemented full reversals, full refunds and cumulative partial refunds.
- Reused the existing Payments, PaymentEvents and AcmeNotifications tables.
- Prevented duplicate financial processing using the external event identifier and existing unique database indexes.
- Persisted payment updates, notification records and financial events within a database transaction.
- Prevented late Payment Advice notifications from overwriting reversed payment states.

### Verification Results

Manual end-to-end testing was performed using PowerShell, the local Acme simulator and SQL Server.

| Scenario | Result |
|---|---|
| Successful reversal | Passed |
| Duplicate reversal | Passed |
| Full refund | Passed |
| Partial refund | Passed |
| Remaining balance refund | Passed |
| Invalid HMAC signature | Passed |
| Late approval after reversal | Passed |
| Financial event verification | Passed |

### Financial Verification

- Approved payments: 4.
- Total authorised amount: R650.
- Reversal events: 1.
- Total reversed amount: R100.
- Refund events: 3.
- Total refunded amount: R500.
- Net payment value: R50.
- Successfully processed reversal and refund notifications: 4.

### Design Decisions and Trade-offs

The implementation intentionally remains within the scope of the technical assessment.

The original authorised amount is preserved for reporting purposes. Refund amounts accumulate independently and cannot exceed the authorised balance.

Notifications received before the original payment outcome is resolved rely on Acme's retry mechanism. A durable deferred-notification worker has not been introduced, meaning notifications could remain unapplied if the provider exhausts its retries.

Webhook deliveries were simulated using signed PowerShell requests. No external payment provider or AWS deployment was required.

No additional database tables or migrations were necessary.

## Stage 5 — Financial Reporting and Regression Testing

### Daily Financial Summary

Implemented the daily financial summary using SQL Server and the existing PaymentEvents ledger.

Location: `database/Reporting/DailyPaymentSummary.sql`

The report provides:

- Reporting date.
- Sub-merchant identifier and trading name.
- Currency code.
- Approved payment count and total.
- Declined payment count and total.
- Reversal count and total.
- Refund count and total.
- Net payment total.

Financial amounts are represented in minor currency units.

The report uses South African Standard Time for business dates while retaining UTC timestamps in the database.

Financial reporting is based on historical payment events rather than the current payment status, ensuring that subsequent reversals and refunds do not remove the original approval from historical reporting.

### Financial Verification

The controlled Stage 4.4 test transactions produced the following verified results:

| Metric | Result |
|---|---:|
| Approved payments | 4 |
| Approved amount | R650.00 |
| Reversal count | 1 |
| Reversal amount | R100.00 |
| Refund count | 3 |
| Refund amount | R500.00 |
| Net payment amount | R50.00 |

### Automated Regression Testing

Executed the entire solution test suite using `dotnet test`.

- Total tests: 43
- Passed: 43
- Failed: 0
- Skipped: 0
- Build: Successful

### Design Decisions

A standalone SQL report was selected to meet the assessment requirements without introducing unnecessary reporting infrastructure.

The net payment amount represents approvals less reversals and refunds. It does not represent actual merchant settlement after processing fees.

AWS deployment is outside the assessment's required implementation scope. The solution will provide a local containerised environment and document the proposed AWS production deployment approach.