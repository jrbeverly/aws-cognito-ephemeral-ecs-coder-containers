# Deployment Workflow

## Purpose

This document explains how the platform is deployed and how a workspace moves through its lifecycle. It is written for operators and contributors who need to deploy the system, understand provisioning behavior, or troubleshoot lifecycle transitions.

This document does not repeat the architecture rationale. See [HLD.md](../HLD.md) for architectural decisions.

## Audience

- Platform operators running `terraform apply` and monitoring workspace health
- Contributors adding or modifying deployment behavior
- Anyone debugging why a workspace is stuck or missing

## Deployment Model

The system uses two separate deployment mechanisms:

| Layer | Tool | Frequency | Authority |
|-------|------|-----------|-----------|
| **Shared Platform** | Terraform | Once per environment (infrequent) | Defines the shared foundation |
| **Per-Workspace Stacks** | CloudFormation | Per-user, on demand | Defines deployed workspace resources |

**Shared platform resources are provisioned once by Terraform. Workspace resources are provisioned per-user by CloudFormation.** The shared platform provides the minimum prerequisites for workspace stacks to operate. It does not own, route, or manage individual workspaces.

For the exact resource boundary between shared platform and per-workspace stacks, see [docs/shared-platform-boundary.md](shared-platform-boundary.md).

---

## Phase 1: Shared Platform Deployment

The shared platform is the foundation deployed once per environment (`prod`, `staging`, `sandbox`).

### What Gets Deployed

| Layer | Resources | Terraform Module |
|-------|-----------|-----------------|
| **Networking** | VPC, public subnets, private subnets, Internet Gateway, NAT Gateway, route tables, ALB security group, ECS tasks security group | `modules/shared-platform/networking` |
| **Hosting** | ECS cluster, Fargate capacity providers, task execution IAM role, task IAM role, CloudWatch log group | `modules/shared-platform/ecs-cluster` |
| **Identity** | Cognito User Pool, User Pool Domain, portal App Client | `modules/shared-platform/cognito` |
| **DNS & TLS** | ACM wildcard certificate (`*.domain`), Route 53 hosted zone, DNS validation records | Managed in `env/shared-platform/prod/main.tf` |
| **Workspace Lookup** | DynamoDB table (`workspace-lookup-{environment}`) with GSI1 for workspace ID queries | Managed in `env/shared-platform/prod/main.tf` |

### Deploy Commands

```bash
make deploy AUTO_APPROVE=1 USER_EMAIL=you@example.com
```

`scripts/deploy.sh` detects the hosted zone, applies Terraform (local state), builds and pushes the workspace-runtime and control-plane images, and rolls the control-plane service. The Terraform config uses a `data` source for the hosted zone; it does not create it.

### Outputs Passed to Workspace Stacks

After `terraform apply`, the shared platform outputs values that workspace CloudFormation stacks consume as parameters:

| Output | Source | Used By Workspace Stack Parameter |
|--------|--------|----------------------------------|
| `vpc_id` | `module.networking` | `VpcId` |
| `public_subnet_ids` | `module.networking` | `PublicSubnetIds` |
| `private_subnet_ids` | `module.networking` | `PrivateSubnetIds` |
| `ecs_tasks_security_group_id` | `module.networking` | `AlbSecurityGroupId` (indirectly, via workspace SG rule) |
| `alb_security_group_id` | `module.networking` | `AlbSecurityGroupId` |
| `cluster_name` | `module.ecs` | `EcsClusterName` |
| `task_execution_role_arn` | `module.ecs` | `EcsTaskExecutionRoleArn` |
| `task_role_arn` | `module.ecs` | `EcsTaskRoleArn` |
| `user_pool_id` | `module.cognito` | `CognitoUserPoolId` |
| `user_pool_arn` | `module.cognito` | `CognitoUserPoolArn` |
| `domain_prefix` | `module.cognito` | `CognitoDomainPrefix` |
| `certificate_arn` | `aws_acm_certificate_validation.workspaces` | `CertificateArn` |
| `hosted_zone_id` | `data.aws_route53_zone.main` | `HostedZoneId` |

Terraform wires these outputs into the control-plane task definition as `WorkspaceStack__*` environment variables (`env/shared-platform/prod/control-plane.tf`).

### Workspace Template

Terraform uploads `env/workspace/template.yaml` to the templates bucket and passes its URL as `WorkspaceStack__TemplateUrl`.

---

## Phase 2: Per-Workspace Deployment

Each user workspace is a separate CloudFormation stack. The Provisioning API (`ProvisioningService.Api`) creates stacks on demand when authenticated users request a workspace. CloudFormation is the authoritative source of truth for what exists — the DynamoDB workspace lookup table is a derived index for UX and cleanup efficiency.

### What Gets Deployed Per Workspace

One CloudFormation stack creates all of the following for a single user:

| Resource | CloudFormation Logical ID | Purpose |
|----------|--------------------------|---------|
| **ALB** | `WorkspaceAlb` | Internet-facing HTTPS endpoint |
| **HTTPS Listener** | `WorkspaceHttpsListener` | Cognito auth + forward to ECS |
| **Target Group** | `WorkspaceTargetGroup` | Routes to ECS task on port 7080 |
| **ECS Service** | `WorkspaceService` | Single Fargate task in shared cluster |
| **Task Definition** | `WorkspaceTaskDefinition` | Coder container, EFS mount, health check |
| **EFS File System** | `WorkspaceFileSystem` | Encrypted persistent storage |
| **EFS Access Point** | `WorkspaceAccessPoint` | POSIX user mapping (uid:gid 1000:1000) |
| **EFS Mount Targets** | `EfsMountTargetA`, `EfsMountTargetB` | One per private subnet |
| **Security Groups** | `WorkspaceSecurityGroup`, `EfsSecurityGroup` | Task ↔ ALB and task ↔ EFS rules |
| **Cognito App Client** | `WorkspaceCognitoClient` | Per-workspace OAuth client for ALB auth |
| **IAM Role** | `WorkspaceTaskRole` | Scoped permissions for workspace runtime |
| **CloudWatch Log Group** | `WorkspaceLogGroup` | Container logs, 7-day retention |
| **Route 53 Record** | `WorkspaceDnsRecord` | `{workspaceId}.{domain}` → ALB |

### Stack Parameters

Workspace stacks consume ~20 parameters. These come from a mix of:

- **Shared platform outputs** (VPC, subnets, ECS cluster, Cognito pool, certificate ARN)
- **Generated per-workspace values** (workspace ID, user email, password)
- **Configured defaults** (task CPU/memory, IAM actions, container image)

The full parameter list is defined in `env/workspace/template.yaml` and enumerated in `WorkspaceStackOptions` at `src/ProvisioningService/ProvisioningService.Api/Services/WorkspaceService.cs:333`.

### Provisioning Flow

1. A user signs in through Cognito at the Portal Frontend.
2. The frontend calls `GET /api/v1/workspaces/me` to check for an existing workspace.
3. If the user has no workspace (or their previous workspace is `failed`), the frontend calls `POST /api/v1/workspaces/`.
4. The Provisioning API:
   - Extracts the user's email from the JWT `email` claim.
   - Checks DynamoDB for an existing non-terminal workspace (status `provisioning`, `active`, or `deleting` blocks new creation).
   - Generates a workspace ID (12 hex chars), a 16-char password, and a stack name (`workspace-{workspaceId}`).
   - Writes a DynamoDB record with status `provisioning`.
   - Submits a `CreateStack` request to CloudFormation with all parameters and tags.
   - Updates the DynamoDB record with the returned `StackId`.
   - Returns `202 Accepted` with the workspace record.
5. CloudFormation provisions all resources. The stack typically completes in 2–5 minutes.
6. The frontend polls `GET /api/v1/workspaces/me` every 3 seconds. During polling, the Provisioning API calls `DescribeStacks` to refresh state from CloudFormation.
7. When CloudFormation reaches `CREATE_COMPLETE`, the API extracts stack outputs (`WorkspaceUrl`, `AlbDnsName`, `EcsServiceName`) and transitions the workspace status to `active`.
8. The frontend detects the `active` status and redirects the user's browser to the workspace URL.

### Provisioning Time Expectations

| Stage | Typical Duration |
|-------|-----------------|
| DynamoDB write + stack submission | < 1 second |
| CloudFormation stack creation | 2–5 minutes |
| ALB provisioning | 1–2 minutes |
| ECS task launch + health check passing | 30–90 seconds |
| DNS propagation | < 60 seconds (within AWS) |
| **Total from POST to active** | **~3–7 minutes** |

The frontend's polling interval is 3 seconds. Once the workspace is `active`, the user is redirected automatically.

---

## Workspace Lifecycle

### Normal Lifecycle States

```
                ┌──────────┐
    User signs  │          │  POST /workspaces
    in, no      │  (none)  │────────────────────▶
    workspace   │          │
                └──────────┘
                                   ┌──────────────────┐
                                   │   provisioning   │
                                   │                  │
                                   └────────┬─────────┘
                                            │
                              CF stack     │ CREATE_COMPLETE
                              submitted    │
                                            ▼
                                   ┌──────────────────┐
                User navigates     │                  │
                to workspace URL ◀─│     active       │
                                   │                  │
                                   └────────┬─────────┘
                                            │
                              Manual      │ MaxAge exceeded
                              delete      │ or idle timeout
                                            │
                                            ▼
                                   ┌──────────────────┐
                                   │    deleting      │
                                   │                  │
                                   └────────┬─────────┘
                                            │
                              Stack       │ Deleted
                              deleted     │
                                            ▼
                                        (record
                                        removed)
```

Any state can transition to `failed` if CloudFormation stack creation fails and enters `ROLLBACK_COMPLETE`.

### State Definitions

| Status | Meaning | User-Visible |
|--------|---------|-------------|
| `provisioning` | CloudFormation stack is creating resources | "Provisioning your workspace..." with spinner |
| `active` | Workspace is ready, endpoint is reachable | "Workspace Ready" with Open button |
| `failed` | Stack creation failed or rolled back | "Provisioning failed" with Create New button |
| `deleting` | Workspace teardown in progress | "Your workspace is being removed" |

### Reconnect Flow

When an authenticated user returns to the portal:

1. Frontend calls `GET /api/v1/workspaces/me`.
2. If the workspace is `active`, the portal displays the workspace URL and an "Open Workspace" button.
3. The user can also navigate directly to `https://{workspaceId}.{domain}` if they know the URL.

If the workspace was `provisioning` during the last session and is still in that state, polling resumes. If the CloudFormation stack completed in the meantime, the status refreshes to `active` on the next `DescribeStacks` call.

### Expiration and Cleanup

Workspaces are disposable. The Cleanup Worker (`CleanupService.Worker`) runs as a scheduled task (typically triggered by EventBridge or a cron schedule) and evaluates every DynamoDB record against the cleanup policy.

**Default Cleanup Policy** (configurable via `CleanupPolicyOptions`):

| Condition | Default Threshold | Action |
|-----------|------------------|--------|
| `provisioning` + no update for N minutes | 30 minutes | Delete stack and DynamoDB record |
| `active` + created more than N hours ago | 8 hours | Delete stack and DynamoDB record |
| `deleting` + no update for N minutes | 60 minutes | Delete stack (retry) and DynamoDB record |
| `failed` + no update for N minutes | 60 minutes | Clean up DynamoDB record |

The cleanup worker:
1. Scans all DynamoDB workspace records.
2. Evaluates each record against the policy.
3. Calls `DeleteStack` on CloudFormation for stacks that still exist.
4. Performs a conditional `DeleteItem` on DynamoDB to remove the record (the condition `WorkspaceId = :wid` prevents races with re-provisioning).

The worker logs a summary after each scan: scanned count, skipped, cleaned, errors, and per-reason counts.

### Manual Delete

Users can delete their own workspace from the portal. The frontend calls `DELETE /api/v1/workspaces/me`, which:

1. Transitions the workspace status to `deleting`.
2. Saves the updated DynamoDB record.
3. Calls `DeleteStack` on the CloudFormation stack.
4. Removes the DynamoDB record.

---

## Observability During Deployment

### ProvisioningService Logs

The API emits structured logs at key points:

```
Provisioning requested for {UserEmail}
Workspace record created {WorkspaceId} for {UserEmail} — initiating CloudFormation stack {StackName}
CloudFormation stack creation submitted {StackName} (StackId: {StackId}) for workspace {WorkspaceId} ({UserEmail})
Workspace {WorkspaceId} ({UserEmail}) transitioned to Active — WorkspaceUrl: {WorkspaceUrl}
```

On failure:
```
CloudFormation stack creation failed for workspace {WorkspaceId} ({UserEmail}) — stack {StackName}
Workspace {WorkspaceId} ({UserEmail}) marked Failed — CloudFormation stack {StackName} status ROLLBACK_COMPLETE
```

### CleanupService Logs

```
Cleanup scan starting
Scanned {Scanned} workspace records — status breakdown: {@StatusCounts}
Cleanup scan complete — scanned: {Scanned}, skipped: {Skipped}, cleaned: {Cleaned}, errors: {Errors}
```

### CloudFormation Console

Each workspace stack is visible in the CloudFormation console with the stack name `workspace-{workspaceId}`. The stack's **Events** tab shows the provisioning timeline. Stack outputs show `WorkspaceUrl`, `AlbDnsName`, and `EcsServiceName`.

### DynamoDB Table

The `workspace-lookup-{environment}` table contains one item per user (PK = `USER#{email}`, SK = `WORKSPACE`). Each item includes:

- `Status`: Current lifecycle state
- `WorkspaceId`, `StackId`, `StackName`: CloudFormation references
- `WorkspaceUrl`, `AlbDnsName`, `EcsServiceName`: Populated when `active`
- `CreatedAt`, `UpdatedAt`: Timestamps for cleanup policy evaluation

The GSI1 index (PK = `WORKSPACE#{workspaceId}`) enables lookup by workspace ID.

### ECS Console

Each workspace ECS service is named `workspace-{workspaceId}` in the shared cluster. The service runs one Fargate task. The task's health check (`/healthz` on port 7080) determines whether the target group considers the task healthy.

---

## Configuration Reference

### ProvisioningService (`appsettings.json` or environment variables)

| Key | Purpose | Required |
|-----|---------|----------|
| `DynamoDb:TableName` | DynamoDB table name | Yes (default: `workspace-lookup`) |
| `Cognito:Authority` | Cognito JWT issuer URL | Yes |
| `Cognito:ClientId` | Cognito app client ID for token validation | No |
| `WorkspaceStack:TemplateUrl` | S3 URL of the CloudFormation template | Yes |
| `WorkspaceStack:VpcId` | Shared platform VPC ID | Yes |
| `WorkspaceStack:PublicSubnetIds` | Comma-separated public subnet IDs | Yes |
| `WorkspaceStack:PrivateSubnetIds` | Comma-separated private subnet IDs | Yes |
| `WorkspaceStack:EcsClusterName` | Shared ECS cluster name | Yes |
| `WorkspaceStack:*` | All other WorkspaceStackOptions fields | See `WorkspaceStackOptions` class |

### CleanupService (`appsettings.json` or environment variables)

| Key | Purpose | Default |
|-----|---------|---------|
| `DynamoDb:TableName` | DynamoDB table name | `workspace-lookup` |
| `CleanupPolicy:MaxAgeHours` | Max workspace age before teardown | `8` |
| `CleanupPolicy:ProvisioningTimeoutMinutes` | Time before stuck provisioning is reaped | `30` |
| `CleanupPolicy:DeletingTimeoutMinutes` | Time before stuck deletion is force-cleaned | `60` |
| `CleanupPolicy:FailedCleanupTimeoutMinutes` | Time before stale failed records are removed | `60` |

---

## Related Documentation

- [HLD.md](../HLD.md) — Architectural decisions and design rationale
- [TECHNICAL.md](../TECHNICAL.md) — Technical implementation guidance
- [docs/shared-platform-boundary.md](shared-platform-boundary.md) — Shared vs. per-workspace resource boundary
- [docs/operator-runbook.md](operator-runbook.md) — Failure diagnosis and recovery procedures
