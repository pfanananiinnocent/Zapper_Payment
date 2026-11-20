
# Incremental Implementation Plan

## Approach

Rather than requesting a complete generated application in a
single prompt, the assignment was divided into smaller
implementation stages.

Each stage was implemented and verified before progressing
to the next major feature.

## Implementation Breakdown

| Stage | Implementation | Validation |
|---|---|---|
| 1 | Solution architecture and project setup | Project references and successful compilation |
| 2 | SQL schema, EF Core and synthetic seed data | Database migrations and record inspection |
| 3.1 | Acme contracts and merchant formatting | Contract and request verification |
| 3.2 | Acme HTTP client and local simulator | Successful provider communication |
| 3.3 | Payment processing and persistence | Approval, decline and duplicate submissions |
| 3.4.1 | Simulator RTTQ and replay handling | Provider query and replay verification |
| 3.4.2 | Application RTTQ integration | Provider status query verification |
| 3.4.3 | Manual reconciliation | Resolution of uncertain payments |
| 3.4.4 | Automated reconciliation | Background recovery verification |
| 3.4.5 | HTTP resilience and concurrency | Timeout, rate limiting and concurrent requests |
| 4.1 | Payment advice contracts and validation | Automated validation tests |
| 4.2 | Basic Authentication | Valid and invalid authentication scenarios |
| 4.3 | Payment advice processing | Duplicate and out-of-order advice |
| 4.4 | Transaction status notifications | HMAC, reversals and refunds |
| 5 | Daily reporting | SQL financial reconciliation |
| 6 | Docker containerisation | Local deployment and persistence |
| 7 | AWS readiness | Documented deployment approach |
| 8 | README | Local execution instructions |
| 9 | AI artifacts | Development workflow documentation |

## Human and AI Responsibilities

AI assisted with:

- Requirements decomposition.
- Proposed architecture.
- Entity and API contract generation.
- Implementation proposals.
- Error investigation.
- Test scenario design.
- SQL verification queries.
- Documentation.

The developer was responsible for:

- Selecting the implementation approach.
- Managing the local development environment.
- Applying source code changes.
- Building and running the application.
- Executing API requests.
- Inspecting database records.
- Running concurrency verification.
- Investigating unexpected behaviour.
- Confirming acceptance results.
- Managing Git commits and repository contents.

## Iterative Delivery

The implementation plan was refined as new integration
behaviours and technical issues were discovered.

For example, reconciliation and outbound resilience were
implemented before notification processing because uncertain
payment outcomes needed to be represented safely.

The implementation was not treated as complete simply because
generated code compiled successfully.

Each important feature required observable runtime behaviour
and, where applicable, matching persisted database records.

## Completion Criteria

The solution was considered functionally implemented when:

- Required payment scenarios were demonstrated.
- Duplicate payment and notification handling was verified.
- Payment lifecycle records remained consistent.
- Financial reporting reconciled with payment events.
- All 43 automated tests passed.
- Docker containers started successfully.
- Database records persisted across container recreation.
- Documentation described the implementation and its limitations.
