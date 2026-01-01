# High-Level Design

## Purpose

This document locks the current high-level technical direction for this repository.

It should answer enough of the major architectural questions that future issue write-ups can assume the core model is already decided. It is intentionally high-level. It defines the intended shape of the system, the boundaries between components, and the main design decisions that should remain stable unless the project direction changes materially.

This document does not try to prescribe every implementation detail. Where an exact mechanism is not yet important, it leaves room for follow-on implementation decisions.

## Design Intent

The system should provide secure, browser-based access to AWS-hosted development and operational tooling without extending the restricted VPC boundary onto a user's local machine.

The platform should:

- authenticate users before they reach workspace infrastructure
- provision isolated workspaces inside AWS
- expose those workspaces through browser-based VS Code provided by Coder
- keep normal workflows inside AWS-managed environments
- clean up workspaces automatically
- stay operationally simple enough to prove and validate the concept quickly

The platform should not attempt to solve every future governance problem in its first shape. The current direction is simplicity-first and concept-validation-first.

## Authoritative Direction

Future issue write-ups should assume the following unless a later document explicitly supersedes them:

- Each user workspace is its own deployment unit.
- Workspace deployments are provisioned and deleted through CloudFormation.
- Each workspace deployment contains its own load balancer path and should not depend on a shared dynamic routing mesh.
- Cognito authentication should be enforced directly by the load balancer where possible.
- The user-facing environment is an ECS-hosted Coder deployment exposed through browser-based VS Code.
- The platform owns workspace lifecycle. Coder does not own workspace lifecycle.
- Cleanup and expiration run as a separate scheduled process, not as a deeply stateful request-path orchestration flow.
- Persistence only needs to be strong enough to survive common task failure or restart scenarios. It does not need to provide durable long-term personal environments.
- One user gets at most one active workspace at a time.
- One workspace gets one storage attachment or storage boundary.
- The current version should not introduce a second dedicated compute plane for long-running jobs unless a real requirement forces it.

## Design Principles

### 1. Simplicity Over Density

The design should prefer operational simplicity over resource packing, quota efficiency, or clever multi-tenant routing.

The expected scale is modest. If needed, multiple copies of the overall system can be deployed rather than compressing everything into a more complicated shared architecture.

### 2. Native Integrations Over Custom Glue

The design should prefer AWS-native integration points when they simplify the platform:

- Cognito integration at the ALB edge
- CloudFormation for provisioning and teardown
- ECS for runtime hosting
- EventBridge for scheduled cleanup

Avoid building large amounts of Lambda code to re-create behaviors already available from managed AWS services.

### 3. Explicit Ownership Boundaries

Each major part of the system should own a clear responsibility:

- the frontend owns user-facing entry and reconnect flow
- the provisioning layer owns workspace lifecycle requests
- CloudFormation owns deployed resource lifecycle
- the workspace runtime owns the browser IDE experience
- the cleanup path owns expiration and teardown

### 4. Resilient Enough, Not Perfect

The persistence model should protect against common operational disruptions such as task restart, accidental OOM, or container rescheduling where practical.

It does not need to preserve user state indefinitely. Workspace expiration and teardown are expected and acceptable.

### 5. Browser Access Only

Normal user access should happen through the browser-based IDE surface. Users should not be given general direct machine access as the normal operating model.

## System Overview

At a high level, the platform consists of four parts:

1. A stable frontend portal where users sign in and initiate or resume workspace access.
2. A thin provisioning layer that creates, looks up, and tears down workspace deployments.
3. Per-user workspace deployments running on ECS and fronted by Cognito-protected load balancers.
4. A scheduled cleanup path that expires and deletes old workspace deployments.

The core user flow is:

1. The user goes to the main site.
2. The user authenticates.
3. The frontend checks whether the user already has an active workspace.
4. If a workspace exists, the user is shown a reconnect path.
5. If no workspace exists, provisioning is triggered automatically.
6. The platform creates a new workspace deployment for that user.
7. The user is redirected to the dedicated workspace URL.
8. The load balancer enforces Cognito authentication and forwards traffic to the user's Coder service.

## High-Level Architecture

```text
User Browser
    |
    v
Frontend Portal
    |
    +--> Workspace Lookup / Provision Request
            |
            v
      Provisioning Layer
            |
            v
      CloudFormation Workspace Stack
            |
            +--> ALB with Cognito Authentication
            +--> ECS Service running Coder
            +--> Per-workspace storage
            +--> DNS record
            +--> IAM and network policy
            |
            v
      Browser-based VS Code Workspace

Background Cleanup
    |
    v
EventBridge Schedule
    |
    v
Cleanup Worker
    |
    v
Delete Expired Workspace Stacks
```

## Major Components

### Frontend Portal

The frontend portal is the human entry point into the system.

It should:

- authenticate the user
- determine whether a workspace already exists
- present a reconnect path when one exists
- trigger provisioning when one does not
- redirect the user to the workspace URL once available

The frontend owns the user-to-workspace routing decision. It does not need to own low-level network routing logic.

### Provisioning Layer

The provisioning layer is a thin control surface for workspace lifecycle requests.

Its responsibilities are:

- accept authenticated create, resume, and teardown actions
- determine whether a user already has an active workspace
- create or delete workspace CloudFormation stacks
- surface basic workspace state back to the frontend

This layer may be implemented with API Gateway and Lambda or an equivalently small service. The key design requirement is that it remains thin and does not grow into a fragmented orchestration mesh.

### CloudFormation Workspace Stack

The workspace stack is the authoritative deployment unit for a single user workspace.

This is a key design decision.

CloudFormation should be the source of truth for:

- which resources belong to a workspace
- how those resources are created
- how they are updated
- how they are deleted during cleanup

Templates may be written directly or generated by a higher-level tool, but the lifecycle boundary should still be a CloudFormation stack.

Each workspace stack should own the resources needed for that single workspace deployment, including:

- the load balancer entry path
- the ECS service and task definition
- the storage component mounted into the workspace
- the workspace DNS record
- security groups and related network boundaries
- IAM roles or policies needed by the runtime
- logging and tagging resources as needed

### Workspace Runtime

The workspace runtime is a user-dedicated ECS service running Coder and exposing browser-based VS Code.

For the purposes of this repository:

- `workspace` is the preferred term for the user-facing environment
- `worker` should not imply a second execution plane in the current version

Coder is treated as the browser IDE application running inside the workspace deployment. It is not the owner of overall lifecycle, routing, or cleanup.

### Cleanup Path

Cleanup is a separate periodic process.

It should:

- identify idle or expired workspaces
- identify orphaned or failed deployments
- trigger teardown by deleting the relevant CloudFormation stacks
- update any derived indexing or lookup metadata if such metadata exists

This path should be driven by EventBridge or a similar scheduler. It should remain separate from the user-facing provisioning path.

## Locked Technical Decisions

### 1. Deployment Model

Each user workspace is provisioned as its own CloudFormation-managed deployment.

This is preferred over a design that relies on a single shared load balancer, a large set of dynamic listener rules, or a custom Lambda-based auth and routing framework.

Rationale:

- cleaner Cognito integration
- simpler ownership and teardown
- easier reasoning about per-user isolation
- easier cleanup through stack deletion
- acceptable operational footprint for the expected scale

### 2. Load Balancer Strategy

The default direction is to treat each workspace deployment as having its own dedicated load balancer path, with Cognito enforced directly at the load balancer edge.

This HLD intentionally does not optimize for minimizing ALB count. It optimizes for simplicity and predictable behavior.

Future issue writers should not assume a shared ALB architecture unless a later decision explicitly changes direction.

### 3. DNS Strategy

The design does not require wildcard DNS routing.

Each workspace may have its own explicit DNS record and be treated as a separate deployment endpoint. A wildcard certificate or other shared certificate approach may still be used if it simplifies TLS management, but wildcard DNS is not a requirement of the architecture.

### 4. Runtime Hosting Model

Workspace runtimes are hosted on ECS as user-dedicated services within a shared platform cluster or equivalent shared ECS hosting boundary.

The important design point is:

- shared cluster
- isolated service per workspace
- isolated storage boundary per workspace

The exact ECS launch mode can remain an implementation decision unless a future requirement makes it architecturally important.

### 5. Persistence Model

Each workspace should have a simple storage component mounted only into that workspace.

The storage goal is limited:

- survive common task restart or replacement scenarios
- reduce user pain from accidental process or container failure
- avoid long-term persistence after workspace expiration

The preferred implementation direction is a managed network-attached storage option that survives task replacement without custom recovery logic. The exact storage product can remain an implementation decision, but the design should favor the simplest option that meets the restart-resilience requirement.

### 6. Workspace Cardinality

Each user gets at most one active workspace at a time.

This simplifies:

- lookup and reconnect logic
- storage ownership
- cleanup behavior
- user expectations

### 7. Cleanup Model

Cleanup should primarily operate by deleting expired or orphaned CloudFormation workspace stacks.

This is the main lifecycle simplification of the architecture. Teardown should happen by removing the deployment unit, not by separately reasoning about every underlying resource.

### 8. Long-Running Compute Scope

This HLD does not define a separate long-running worker plane.

If a future need emerges for workloads that clearly outlive the workspace model, that can be added later as a separate architectural decision. It should not be assumed by default in early implementation work.

## Identity And Access Model

The identity model is intentionally straightforward.

- users authenticate before accessing workspace infrastructure
- the workspace endpoint is protected by Cognito at the load balancer edge
- the workspace runtime receives only scoped AWS permissions needed for approved workflows
- no direct VPN access is granted as part of the normal model

User access to the workspace should happen through the browser-based Coder interface. Direct administrative access to the underlying runtime should be exceptional and tightly controlled, not part of the normal product shape.

## Lifecycle Model

### Provisioning

Workspace provisioning should be initiated only for authenticated users.

Provisioning is expected to:

- resolve the requesting user identity
- determine whether an active workspace already exists
- create a new CloudFormation workspace stack if needed
- return workspace state to the caller

Provisioning may be asynchronous from the user's perspective. The frontend should tolerate a short creation window and present appropriate status.

### Reconnect

Reconnect should be simple:

- the user returns to the portal and is offered the active workspace
- or the user returns directly to the workspace URL if it still exists

No separate session-routing broker is required beyond the portal lookup and the dedicated workspace endpoint.

### Expiration

Workspaces are expected to expire.

Expiration should be based on policy such as:

- idle timeout
- maximum age
- explicit user teardown
- administrative cleanup

This HLD does not lock exact timeout values. It locks the expectation that workspaces are disposable and that cleanup is automatic.

## Persistence Expectations

The persistence model should be interpreted narrowly.

The system should attempt to preserve useful workspace state across:

- container crash
- accidental OOM kill
- ECS task restart
- routine rescheduling where the selected storage approach supports it

The system does not need to preserve state across:

- planned expiration
- explicit teardown
- long inactive periods
- broad platform recycling events

The intended user expectation is:

- short-term resilience, yes
- long-term personal environment retention, no

## Security And Governance Posture

This platform improves containment and governance, but it is not a perfect data-loss prevention system.

The current goal is to keep normal work inside AWS-hosted browser workspaces and reduce unnecessary movement of sensitive data onto unmanaged local machines.

Users may still be capable of actions such as:

- downloading files
- copying content
- taking screenshots
- publishing data externally if separate controls do not block that

That is acceptable for the current phase. The architecture should be judged by whether it materially improves the governance boundary while preserving enough usability that users do not need to bypass it.

## Source Of Truth

CloudFormation is the authoritative source of truth for deployed workspace infrastructure.

If the system later introduces supporting metadata for faster lookup or user experience reasons, that metadata should be treated as a derived index or cache rather than the authoritative description of what exists.

This principle matters most for:

- cleanup correctness
- ownership tracing
- teardown safety
- operational debugging

## Observability Expectations

The initial platform should provide enough visibility to answer:

- which user owns which workspace
- whether a workspace is provisioning, active, expired, or failed
- which stack backs a workspace
- when a workspace was created and last used
- why provisioning or cleanup failed

This does not require a highly elaborate observability platform in the first version, but it does require consistent tagging, structured status reporting, and basic logs for provisioning and cleanup actions.

## Explicit Non-Goals

The current high-level design does not attempt to provide:

- perfect prevention of all data exfiltration paths
- a general-purpose bastion host model
- permanent personal developer servers
- a shared multi-tenant workspace routing fabric
- a complex multi-function orchestration system for routine lifecycle actions
- a separate managed long-running compute plane in the initial version
- ultra-high-scale optimization for thousands of users per second

## Follow-On Issue Assumptions

Future issue authors should assume these defaults:

- new workspace infrastructure work should fit the per-workspace CloudFormation deployment model
- new access-flow work should preserve ALB plus Cognito simplicity
- new runtime work should assume ECS-hosted Coder as the user-facing environment
- new cleanup work should assume scheduled stack deletion as the primary teardown model
- new persistence work should optimize for simple restart resilience, not durable personal state
- new architecture work should justify itself if it introduces shared routing complexity or a second compute plane

## Remaining Flexible Areas

The following areas remain intentionally open unless and until they become important enough to lock:

- exact frontend hosting approach
- exact implementation of the provisioning API
- exact ECS launch mode
- exact managed storage product and sizing choices
- exact DNS naming format
- exact timeout values for expiration
- exact authoring tool used to generate CloudFormation templates

These should be decided by implementation issues when they become relevant, provided they do not violate the architectural direction in this HLD.

## Overall Judgment

This design is intentionally conservative.

It trades some theoretical efficiency for a much cleaner operating model:

- one workspace, one deployment unit
- one lifecycle boundary, CloudFormation
- one primary runtime model, ECS-hosted Coder
- one primary access model, ALB plus Cognito
- one cleanup model, scheduled teardown

That is the right trade for the current stage of the project because the immediate goal is to prove the concept, validate the construct, and keep the system simple enough that future implementation work remains straightforward.
