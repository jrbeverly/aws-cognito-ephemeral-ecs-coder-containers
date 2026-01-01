# Technical

## Core AWS Services

The architecture should primarily use the following services and components:

- AWS ECS
- Coder
- Application Load Balancers
- AWS Cognito
- CloudFormation
- EventBridge
- IAM
- Route 53 or equivalent DNS management

A thin provisioning API layer may be implemented with API Gateway and Lambda or an equivalently small service if needed.

## Access Model

- Do not provide direct VPN access to the restricted VPC.
- User access should be mediated through AWS-hosted infrastructure.
- Users should interact with the environment through browser-based tooling rather than local network access.
- Sensitive workflows should remain inside AWS-managed boundaries.

## Authentication

- Use AWS Cognito for user authentication.
- Users should authenticate before any workspace is provisioned or accessed.
- Authentication should integrate with the web application entry point and the workspace load balancer path.
- Prefer load balancer-native Cognito integration over custom authentication code where possible.

## Provisioning And Lifecycle

- Workspace provisioning should be tied to authenticated user identity.
- The platform should maintain a thin provisioning and lifecycle layer rather than a heavily fragmented orchestration system.
- Workspace infrastructure should be provisioned and deleted as a CloudFormation deployment unit.
- Cleanup and expiration should run as a separate scheduled process rather than as a complex synchronous request flow.

## User Workspace Runtime

- Use AWS ECS for the user-facing workspace runtime.
- Each user should receive an isolated ECS-backed workspace deployment.
- The workspace should run Coder.
- Coder should provide the browser-based VS Code environment.
- A user should have at most one active workspace at a time.

## Network Entry

- Application Load Balancers should be used for workspace entry.
- The load balancer should enforce Cognito authentication directly where possible.
- Each workspace deployment should have its own dedicated endpoint or load balancer path.
- The design does not require a shared wildcard routing model.

## Workspace Persistence

Workspace state should be simple and short-lived.

The platform should support:

- Survival of common task restart scenarios
- Recovery from accidental OOM or container replacement with minimal user pain
- Per-workspace storage boundaries
- Expiration of state when the workspace is torn down

The architecture should not assume durable long-term personal environments.

## Workspace Permissions

Workspace containers should have controlled permissions.

They may require:

- Limited IAM permissions
- Access to approved internal services
- Controlled access to internal infrastructure required for approved workflows

Permissions should be scoped to the intended operational use cases and should not create broad unmanaged access.

## Cleanup And Expiration

- Workspaces should remain disposable.
- The platform should support automatic reaping and explicit lifecycle enforcement.
- EventBridge or a similar scheduler should trigger periodic cleanup.
- Cleanup should primarily operate by deleting expired or orphaned workspace deployment stacks.

## Future Execution Extension

The initial architecture does not require a separate managed worker plane.

If future workloads clearly need a runtime that outlives the managed workspace model, that should be introduced later as a separate design concern rather than treated as a built-in requirement from day one.

## Runtime Interaction Pattern

The intended technical interaction is:

1. A user authenticates through Cognito.
2. The frontend checks for an existing active workspace.
3. A thin provisioning layer creates or finds the user's workspace deployment.
4. A CloudFormation-managed workspace stack provides the ECS-hosted Coder runtime and its endpoint.
5. The user interacts with the workspace through the browser behind a Cognito-protected load balancer.
6. The workspace uses controlled IAM permissions to interact with approved internal systems.
7. A scheduled cleanup process tears the workspace down when it expires or is no longer needed.
