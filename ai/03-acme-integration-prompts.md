
# AI-Assisted Engineering Prompts

## Introduction

The following prompts represent the engineering discussions
and instructions used during the development of this assignment.

They have been consolidated and reconstructed for readability
and are not verbatim conversation transcripts.

ChatGPT was used throughout the implementation as a collaborative
engineering assistant.

## Prompt 1 - Requirements Analysis and Architecture

Act as an experienced Lead Software Engineer.

Analyse the supplied Zapper technical assessment, including
the Acme Pay integration specification.

Identify the functional requirements, technical constraints,
integration risks and expected deliverables.

Propose a maintainable .NET 8 architecture using SQL Server
and Entity Framework Core.

Break the implementation into manageable stages.

Keep the solution appropriate for a technical assessment
and avoid unnecessary production infrastructure.

## Prompt 2 - Acme Pay Integration

Using Appendix A of the assessment, assist with implementing
the Acme Pay HTTP client and request/response contracts.

Ensure the implementation respects:

- Required JSON property names.
- Bearer authentication.
- Merchant and transaction identifiers.
- Unique message identifiers.
- Integer monetary amounts.
- South African timestamp formatting.
- Card acceptor name formatting.
- Stored card token processing.
- Provider response and error codes.
- Unknown response fields.

Explain the implementation and the responsibility of each
component before progressing.

## Prompt 3 - Payment Processing and Idempotency

Design a payment processing service that persists the
payment and its provider transaction identifier before
submitting the request to Acme.

Prevent multiple financial transactions when the same
app payment identifier is submitted repeatedly or concurrently.

Use database-level uniqueness constraints rather than
relying exclusively on application-memory checks.

Distinguish between successful HTTP communication and
actual financial approval.

Provide API requests and SQL queries to verify the
implementation.

## Prompt 4 - Timeout Handling and Reconciliation

Review the Acme timeout and Real-Time Transaction Query
requirements.

Design a safe approach for handling payments where the
provider response is not received.

Do not assume that a timeout means the financial
transaction failed.

Retain the original transaction identifier and use RTTQ
to establish the actual payment outcome.

Implement manual and automated reconciliation without
blindly submitting another financial transaction.

Provide verification scenarios for approved, processing,
unknown and timed-out transactions.

## Prompt 5 - HTTP Resilience and Concurrent Requests

Review the Acme provider's rate limit and concurrent
request requirements.

Propose a shared outbound request mechanism for payment
and RTTQ traffic.

Consider:

- Provider rate limits.
- Concurrent request limits.
- Bounded queueing.
- HTTP connection pooling.
- Request timeouts.
- HTTP 429 and Retry-After handling.
- HTTP 5xx responses.
- Requests that were never dispatched.
- Requests with uncertain financial outcomes.

Avoid automatic financial retries that could result
in duplicate payments.

## Prompt 6 - Concurrent Idempotency Verification

Create a PowerShell testing utility that can submit
multiple concurrent payment requests.

Support both unique app payment identifiers and repeated
submissions using the same app payment identifier.

Capture response statuses, transaction identifiers,
request durations and execution summaries.

Use database records and Acme simulator metrics to verify
whether duplicate financial transactions occurred.

Restrict the testing utility to the local assessment
environment.

## Prompt 7 - Payment Advice Notifications

Implement the Acme payment advice notification contract
according to Appendix A.

Use HTTP Basic Authentication and validate the incoming
notification fields.

Ensure repeated notifications do not create duplicate
payment lifecycle records.

Protect confirmed payment states from conflicting or
out-of-order notifications.

Provide automated authentication and validation tests,
as well as manual API and SQL verification scenarios.

## Prompt 8 - Transaction Status Notifications

Implement authenticated transaction status notifications
using the HMAC-SHA256 signature mechanism described in
the Acme specification.

Validate signatures against the exact request body bytes.

Support payment reversals, partial refunds and full refunds.

Prevent duplicate financial movements and invalid payment
lifecycle transitions.

Provide test scenarios for invalid signatures, duplicate
notifications and multiple partial refunds.

## Prompt 9 - Financial Reporting

Design a daily SQL financial summary grouped by
sub-merchant, reporting date and currency.

Include counts and total values for approvals, declines,
reversals and refunds.

Use payment lifecycle events rather than only the current
payment status so historical financial movements are preserved.

Account for South African reporting dates while retaining
UTC timestamps in the database.

Verify the results against actual test transactions.

## Prompt 10 - Containerisation

Containerise the Payments API, Acme simulator and SQL Server
using Docker and Docker Compose.

Provide a locally runnable environment with:

- Environment-based configuration.
- Database migrations and synthetic seed data.
- Persistent SQL Server storage.
- Health verification.
- Local network communication between containers.

Verify that payment records and idempotency behaviour
remain consistent after container recreation.

Do not provision live AWS infrastructure.

## Prompt 11 - Troubleshooting and Reviewing Generated Work

Review the actual source code, configuration and runtime
output provided by the developer.

Identify the root cause of failures before proposing changes.

Prefer targeted corrections over unnecessary rewrites.

For example, investigate why Basic Authentication works
outside Docker but returns HTTP 401 inside the container.

Compare the authentication configuration section against
the Docker environment variable names.

Provide verification steps and expected outcomes.

## Prompt 12 - AWS Production Readiness

Describe how the existing containerised solution could
be deployed to AWS.

Cover deployment, configuration, secrets management,
horizontal scaling, graceful shutdown, monitoring and
payment recovery.

Identify limitations of instance-local rate limiting and
background reconciliation when running multiple replicas.

Document production considerations without implementing
unnecessary cloud infrastructure.

## Original Scope Instruction

The developer explicitly provided the following direction:

"we are not building a production solution, this is just an
assessment they want to use to rate the interview they wont
deploy this to production. lets stick to what they asked on
the requirements and not over solution or complicate it."

This guided the implementation and prevented unnecessary
infrastructure complexity.

## Validation Summary

AI-generated implementations were not accepted solely
because they compiled.

The developer validated the implementation through:

- Local solution builds.
- API requests and response inspection.
- SQL Server record inspection.
- Concurrent payment submission tests.
- Provider simulator diagnostics.
- Notification authentication tests.
- Payment lifecycle verification.
- Financial reporting verification.
- Docker container recreation.
- Automated regression testing.

The final regression run completed with 43 passing tests,
zero failures and zero skipped tests.

The developer retained responsibility for implementation
decisions, runtime verification and Git commits.
