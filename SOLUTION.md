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
