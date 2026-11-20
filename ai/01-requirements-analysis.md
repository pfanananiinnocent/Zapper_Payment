
# Requirements Analysis

## Context Provided to AI

The original Zapper Team Lead Software Engineer Technical Vetting
Assignment was provided to ChatGPT for analysis.

This included Appendix A, which contains the Acme Pay API
integration specification.

The relevant technology constraints and local development
environment were also established before implementation.

## Development Environment

- C# and .NET 8.
- ASP.NET Core Web API.
- SQL Server 2022.
- Entity Framework Core.
- Visual Studio 2022.
- PowerShell.
- Git and GitHub.
- Docker and Docker Compose.
- xUnit.

## Functional Requirements

The assignment was decomposed into the following functional
requirements:

- Submit payments using stored customer card tokens.
- Resolve customer and sub-merchant information.
- Construct valid Acme Pay payment requests.
- Persist payment records and provider identifiers.
- Retrieve current payment status.
- Prevent duplicate payment submissions.
- Handle successful and declined transactions.
- Handle provider timeouts and uncertain outcomes.
- Process authenticated payment advice notifications.
- Process authenticated transaction status notifications.
- Handle duplicate and out-of-order notifications.
- Process reversals and refunds.
- Provide daily financial reporting.

## Technical Requirements

The following technical concerns were identified:

- Correct implementation of the Acme Pay API contract.
- Stable transaction identifiers.
- Database-level uniqueness constraints.
- Payment lifecycle integrity.
- Provider rate limiting.
- Concurrent outbound request handling.
- HTTP connection management.
- Auditability and diagnostic information.
- Secure notification authentication.
- Local containerised execution.
- AWS production-readiness documentation.

## Scope Management

During development, unnecessary production infrastructure
complexity was deliberately excluded.

An original developer instruction during the assessment was:

"we are not building a production solution, this is just an
assessment they want to use to rate the interview they wont
deploy this to production. lets stick to what they asked on
the requirements and not over solution or complicate it."

This instruction guided subsequent engineering decisions.

The implementation remained focused on the requested assessment
capabilities rather than attempting to create an enterprise-wide
payment platform.

## Excluded Implementation Scope

The following capabilities were not implemented as production
infrastructure:

- Live AWS deployments.
- Infrastructure as code.
- Kubernetes orchestration.
- Distributed caching infrastructure.
- Distributed messaging platforms.
- Customer onboarding.
- Merchant onboarding.
- Authentication for callers of the payment submission API.

Production considerations were documented where relevant,
rather than introducing unnecessary dependencies.

Additional concurrency verification was performed voluntarily
to validate payment correctness and outbound request behaviour.

This was supplementary engineering evidence, not a mandatory
load-testing deliverable.

## Requirements Interpretation

A key engineering distinction was that a successful HTTP
response does not necessarily represent an approved payment.

The Acme transaction outcome must be interpreted using the
provider's transaction status and response codes.

Similarly, an outbound timeout does not necessarily mean that
the payment failed.

These distinctions influenced the payment lifecycle,
idempotency design and reconciliation implementation.
