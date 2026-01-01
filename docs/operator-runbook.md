# Operator Runbook

## Purpose

This runbook helps operators diagnose and recover from common failure modes in provisioning, endpoint access, and cleanup. It assumes the reader has access to AWS console (CloudFormation, ECS, DynamoDB, CloudWatch), the ProvisioningService logs, and the CleanupService logs.

This document references the actual status surfaces, log messages, and data structures implemented in the codebase. It does not assume tribal knowledge.

## Audience

- On-call operators responding to workspace provisioning failures
- Contributors debugging why a workspace is unreachable
- Anyone needing to manually intervene in workspace lifecycle

---

## 1. System Health Check

Before investigating a specific workspace failure, verify the shared platform is healthy.

### 1.1 Verify the Provisioning API is Reachable

```bash
curl -s https://api.{domain}/health
```

If unreachable, check the Lambda or ECS service hosting the Provisioning API.

### 1.2 Verify the DynamoDB Table Exists

```bash
aws dynamodb describe-table \
  --table-name workspace-lookup-prod \
  --query 'Table.TableStatus'
```

Expected: `ACTIVE`.

### 1.3 Verify the ECS Cluster is Active

In the ECS console, the shared cluster should show status `ACTIVE` with registered capacity providers.

### 1.4 Verify the Cognito User Pool is Reachable

Open `https://{cognito_domain_prefix}.auth.{region}.amazoncognito.com/login?client_id={portal_client_id}&response_type=code&scope=email+openid+profile&redirect_uri=https://{domain}/callback` in a browser. The Cognito hosted UI should render.

### 1.5 Verify the Cleanup Worker Ran Recently

The Cleanup Worker is a one-shot process triggered by schedule (EventBridge or cron). Check its execution logs in CloudWatch for the most recent `Cleanup scan complete` message. If no runs in the last 24 hours, check the scheduler.

---

## 2. Provisioning Failures

### 2.1 Symptom: User Sees "Provisioning Failed" in Portal

The portal displays `status === 'failed'` with "Workspace provisioning failed. You can try creating a new one."

### 2.2 Diagnosis Steps

**Step 1: Find the workspace record.**

```bash
aws dynamodb get-item \
  --table-name workspace-lookup-prod \
  --key '{"PK":{"S":"USER#user@example.com"},"SK":{"S":"WORKSPACE"}}'
```

Note the `WorkspaceId`, `StackId`, and `Status`.

**Step 2: Check the CloudFormation stack.**

```bash
aws cloudformation describe-stacks \
  --stack-name workspace-{workspaceId} \
  --query 'Stacks[0].StackStatus'
```

**Step 3: If the stack exists, check its events for the failure reason.**

```bash
aws cloudformation describe-stack-events \
  --stack-name workspace-{workspaceId} \
  --query 'StackEvents[?ResourceStatus==`CREATE_FAILED`].[LogicalResourceId,ResourceStatusReason]'
```

**Step 4: Check ProvisioningService logs for the failure.**

Search CloudWatch for:
```
CloudFormation stack creation failed for workspace {workspaceId}
```

The error message and stack trace in the log entry tell you what exception CloudFormation threw.

### 2.3 Common Causes and Resolutions

| Cause | Symptoms | Resolution |
|-------|----------|------------|
| **VPC subnet capacity exhausted** | `CREATE_FAILED` on `EfsMountTargetA` or `WorkspaceService` — "subnet has insufficient IP addresses" | Add more subnets or clean up orphaned EFS mount targets from deleted stacks |
| **ECS task never becomes healthy** | Stack `CREATE_COMPLETE` but workspace stays `provisioning`; task health check failing | Check ECS service events and task logs; verify container image is pullable and Coder starts |
| **CloudFormation template URL unreachable** | `CREATE_FAILED` immediately — "TemplateURL must reference a valid S3 object" | Verify the template is uploaded: `aws s3 ls s3://{bucket}/workspace-template/template.yaml` |
| **IAM permissions missing** | `CREATE_FAILED` with `AccessDenied` | Verify the ProvisioningService's IAM role has `cloudformation:CreateStack`, `cloudformation:DescribeStacks`, `cloudformation:DeleteStack` |
| **Certificate not validated** | `CREATE_FAILED` on `WorkspaceHttpsListener` — "Certificate not found or not validated" | Verify ACM certificate validation completed; check DNS validation records |

### 2.4 Recovery

If the user needs a workspace and the stack failed:

1. **Delete the failed stack manually** (if it exists and is in a terminal state like `ROLLBACK_COMPLETE`):
   ```bash
   aws cloudformation delete-stack --stack-name workspace-{workspaceId}
   ```

2. **OR let the cleanup worker handle it**: failed workspaces are cleaned up after `FailedCleanupTimeoutMinutes` (default 60 minutes) from their last update. The user can create a new workspace immediately — the Provisioning API allows re-provisioning over a `failed` record.

3. **User action**: The user clicks "Create New Workspace" in the portal. The Provisioning API overwrites the `failed` record with a new `provisioning` record (new workspace ID, new stack).

### 2.5 Stuck in Provisioning

If a workspace stays `provisioning` for more than the expected 7 minutes but the CloudFormation stack shows `CREATE_COMPLETE`:

The Provisioning API refreshes status from CloudFormation on each `GET /api/v1/workspaces/me` call. The refresh logic is in `WorkspaceService.RefreshFromCloudFormation()`.

**Check if the refresh is working:**

Look for this log entry in the ProvisioningService:
```
CloudFormation stack {StackName} status CREATE_COMPLETE for workspace {WorkspaceId}
```

If the log shows `CREATE_COMPLETE` but the DynamoDB record still says `provisioning`, the stack outputs may not include the expected keys (`WorkspaceUrl`, `AlbDnsName`, `EcsServiceName`). Check the stack outputs:

```bash
aws cloudformation describe-stacks \
  --stack-name workspace-{workspaceId} \
  --query 'Stacks[0].Outputs'
```

**Manual fix**: Update the DynamoDB record directly:
```bash
aws dynamodb update-item \
  --table-name workspace-lookup-prod \
  --key '{"PK":{"S":"USER#user@example.com"},"SK":{"S":"WORKSPACE"}}' \
  --update-expression 'SET #s = :s, UpdatedAt = :t' \
  --expression-attribute-names '{"#s":"Status"}' \
  --expression-attribute-values '{":s":{"S":"active"},":t":{"S":"2026-01-01T00:00:00.0000000Z"}}'
```

If the stack is `CREATE_IN_PROGRESS` for an unusually long time (> 15 minutes), check:
1. The ECS service deployment events for task launch failures
2. The task's health check — if it never passes, the stack may eventually roll back
3. CloudFormation Events tab for the specific resource that is stuck

---

## 3. Endpoint Access Failures

### 3.1 Symptom: User Cannot Reach Workspace URL

The user navigates to `https://{workspaceId}.{domain}` and sees one of:
- Browser SSL/TLS error
- HTTP 502/503/504
- Cognito login loop
- "Page not found" (DNS error)

### 3.2 Diagnosis Steps

**Step 1: Verify DNS resolution.**

```bash
dig {workspaceId}.{domain}

# Or with AWS CLI
aws route53 list-resource-record-sets \
  --hosted-zone-id {hosted_zone_id} \
  --query "ResourceRecordSets[?Name=='{workspaceId}.{domain}.']"
```

The DNS record should exist and point to the workspace ALB.

**Step 2: Verify the ALB is active.**

```bash
aws elbv2 describe-load-balancers \
  --names {workspaceId}-alb \
  --query 'LoadBalancers[0].State.Code'
```

Expected: `active`.

**Step 3: Verify the target group is healthy.**

```bash
# Get the target group ARN
TG_ARN=$(aws elbv2 describe-target-groups \
  --names {workspaceId}-tg \
  --query 'TargetGroups[0].TargetGroupArn' \
  --output text)

# Check health
aws elbv2 describe-target-health \
  --target-group-arn $TG_ARN \
  --query 'TargetHealthDescriptions[0].TargetHealth.State'
```

Expected: `healthy`.

**Step 4: If unhealthy, check the ECS service.**

```bash
aws ecs describe-services \
  --cluster workspace-cluster \
  --services workspace-{workspaceId} \
  --query 'services[0].[status, runningCount, pendingCount, events[0:3]]'
```

If `runningCount` is 0, check the service events for why the task isn't launching. Common reasons:
- Insufficient CPU/memory in the cluster
- Container image pull failure (ECR permissions)
- Task definition references a deleted resource

**Step 5: Check the Cognito authentication configuration.**

Verify the ALB listener rule references the correct Cognito User Pool, app client, and domain:

```bash
aws elbv2 describe-listeners \
  --load-balancer-arn {alb_arn} \
  --query 'Listeners[0].DefaultActions'
```

The first action should be `authenticate-cognito` with the correct `UserPoolArn`, `UserPoolClientId`, and `UserPoolDomain`.

### 3.3 Common Causes and Resolutions

| Cause | Symptoms | Resolution |
|-------|----------|------------|
| **DNS record not created** | `NXDOMAIN` on dig | Check that CloudFormation stack created `WorkspaceDnsRecord`; verify hosted zone ID is correct in stack parameters |
| **ALB target group unhealthy** | HTTP 502/503, target health `unhealthy` | Check ECS task is running; verify health check path `/healthz` returns 200 on port 7080; check container logs |
| **Cognito misconfiguration** | Cognito login redirect loop | Verify `CallbackURLs` in `WorkspaceCognitoClient` matches `https://{workspaceId}.{domain}/oauth2/idpresponse` |
| **Certificate mismatch** | Browser SSL warning | ACM certificate uses `*.{domain}` wildcard; verify the workspace subdomain matches the wildcard |
| **ALB security group blocks traffic** | Connection timeout on HTTPS | Verify ALB security group allows inbound 443 from 0.0.0.0/0 |
| **Workspace task OOM** | Task stops and restarts, brief 503s | Increase `TaskMemory` in `WorkspaceStackOptions` |

### 3.4 Recovery

If the workspace infrastructure is healthy but the DNS record is missing, the user can use the ALB DNS name directly (temporarily):

```bash
aws cloudformation describe-stacks \
  --stack-name workspace-{workspaceId} \
  --query 'Stacks[0].Outputs[?OutputKey==`AlbDnsName`].OutputValue' \
  --output text
```

The URL would be `https://{alb_dns_name}` but Cognito callback URLs won't match, so this is for diagnosis only.

---

## 4. Cleanup Failures

### 4.1 Symptom: Workspaces Not Being Cleaned Up

Workspaces older than `MaxAgeHours` (default 8 hours) remain in DynamoDB and their CloudFormation stacks still exist.

### 4.2 Diagnosis Steps

**Step 1: Verify the Cleanup Worker is running.**

Check CleanupService logs for `Cleanup scan starting` and `Cleanup scan complete`. If missing, the scheduler may not be triggering it.

**Step 2: Check the cleanup scan summary.**

The log line `Cleanup scan complete — scanned: {Scanned}, skipped: {Skipped}, cleaned: {Cleaned}, errors: {Errors}` tells you how many records were processed and how many had errors.

**Step 3: Check for deletion errors.**

Look for:
```
Failed to clean up workspace {WorkspaceId} ({Status}) for {UserEmail} — reason: {Reason}
```

The exception message follows. Common causes:
- `AccessDenied` — CleanupService IAM role lacks `cloudformation:DeleteStack` permission
- Stack has termination protection enabled
- Stack is in a state that blocks deletion (e.g., `DELETE_FAILED`)

**Step 4: Verify policy thresholds.**

The policies that trigger cleanup are in `CleanupPolicyOptions`:

| Status | Condition | Default |
|--------|-----------|---------|
| `provisioning` | `now > UpdatedAt + ProvisioningTimeoutMinutes` | 30 min |
| `active` | `now > CreatedAt + MaxAgeHours` | 8 hours |
| `deleting` | `now > UpdatedAt + DeletingTimeoutMinutes` | 60 min |
| `failed` | `now > UpdatedAt + FailedCleanupTimeoutMinutes` | 60 min |

If a workspace was updated recently, it may not yet be eligible for cleanup.

### 4.3 Common Causes and Resolutions

| Cause | Symptoms | Resolution |
|-------|----------|------------|
| **Cleanup Worker not scheduled** | No logs for > 1 day | Verify EventBridge rule or cron schedule is active; check CleanupService deployment |
| **Insufficient IAM permissions** | `AccessDenied` in cleanup error logs | Add `cloudformation:DeleteStack` and `cloudformation:DescribeStacks` to CleanupService task role |
| **Stack in DELETE_FAILED** | Record stuck in `deleting` for hours | Manually delete the stack from CloudFormation console, then remove the DynamoDB record |
| **Large scan volume** | Warning: "Scanned {Count} workspace records — consider adding a status-indexed GSI" | If scan count consistently exceeds a few hundred, add a GSI on Status for efficient querying |
| **Race with re-provisioning** | `ConditionalCheckFailedException` (info-level, not an error) | This is expected — the conditional delete prevents removing a record that was re-provisioned |

### 4.4 Manual Cleanup

If the cleanup worker can't delete a stack, manually delete it:

```bash
# 1. Find the stack name from DynamoDB
aws dynamodb get-item \
  --table-name workspace-lookup-prod \
  --key '{"PK":{"S":"USER#user@example.com"},"SK":{"S":"WORKSPACE"}}' \
  --query 'Item.StackName.S' --output text

# 2. Delete the stack
aws cloudformation delete-stack --stack-name workspace-{workspaceId}

# 3. Wait for deletion (or check progress)
aws cloudformation wait stack-delete-complete --stack-name workspace-{workspaceId}

# 4. Remove the DynamoDB record
aws dynamodb delete-item \
  --table-name workspace-lookup-prod \
  --key '{"PK":{"S":"USER#user@example.com"},"SK":{"S":"WORKSPACE"}}'
```

### 4.5 Orphaned Resources

If a DynamoDB record was deleted but the CloudFormation stack still exists, the stack is orphaned. Find these with:

```bash
aws cloudformation describe-stacks \
  --query "Stacks[?starts_with(StackName,'workspace-')].[StackName,StackStatus,CreationTime]" \
  --output table
```

Cross-reference against DynamoDB to find stacks with no record. Delete them manually or let the next cleanup scan handle them (if the cleanup worker can discover stacks independently — the current implementation scans DynamoDB, not CloudFormation).

To find EFS file systems that may have been left behind after a stack deletion failure:

```bash
aws efs describe-file-systems \
  --query "FileSystems[?starts_with(Name,'workspace-')].[FileSystemId,Name,LifeCycleState]" \
  --output table
```

---

## 5. Manual Workspace Teardown

### 5.1 Complete Manual Teardown (Emergency)

To immediately take down a workspace:

```bash
WORKSPACE_ID="a1b2c3d4e5f6"

# 1. Delete the CloudFormation stack (cascading delete of all resources)
aws cloudformation delete-stack --stack-name workspace-${WORKSPACE_ID}

# 2. Wait for completion
aws cloudformation wait stack-delete-complete --stack-name workspace-${WORKSPACE_ID}

# 3. Remove the DynamoDB record (find the user email first)
USER_EMAIL=$(aws dynamodb query \
  --table-name workspace-lookup-prod \
  --index-name GSI1 \
  --key-condition-expression 'GSI1PK = :pk' \
  --expression-attribute-values '{":pk":{"S":"WORKSPACE#'${WORKSPACE_ID}'"}}' \
  --query 'Items[0].UserEmail.S' --output text)

aws dynamodb delete-item \
  --table-name workspace-lookup-prod \
  --key '{"PK":{"S":"USER#'${USER_EMAIL}'"},"SK":{"S":"WORKSPACE"}}'
```

### 5.2 Bypassing a Stuck Deletion

If `DeleteStack` fails repeatedly, use the CloudFormation console to:
1. Select the stack → **Stack actions** → **Delete stack**
2. If "DELETE_FAILED": identify the failing resource in the Events tab.
3. Manually delete the resource from its respective AWS console.
4. Retry stack deletion (CloudFormation skips already-deleted resources).

---

## 6. Observability Reference

### 6.1 Key Log Messages

**ProvisioningService** (`ProvisioningService.Api`):

| Event | Log Pattern | Level |
|-------|------------|-------|
| Create request received | `Provisioning requested for {UserEmail}` | Info |
| Workspace already exists | `Provisioning blocked for {UserEmail} — existing workspace {WorkspaceId} has status {Status}` | Warning |
| DynamoDB record created | `Workspace record created {WorkspaceId} for {UserEmail}` | Info |
| Stack creation submitted | `CloudFormation stack creation submitted {StackName} (StackId: {StackId})` | Info |
| Stack creation failed | `CloudFormation stack creation failed for workspace {WorkspaceId}` | Error |
| Workspace ready | `Workspace {WorkspaceId} ({UserEmail}) transitioned to Active` | Info |
| Stack rolled back | `Workspace {WorkspaceId} ({UserEmail}) marked Failed — CloudFormation stack {StackName} status ROLLBACK_COMPLETE` | Warning |
| No email claim | `Create workspace rejected — no email claim in token` | Warning |

**CleanupService** (`CleanupService.Worker`):

| Event | Log Pattern | Level |
|-------|------------|-------|
| Scan start | `Cleanup scan starting` | Info |
| Scan summary | `Scanned {Scanned} workspace records — status breakdown: {@StatusCounts}` | Info |
| Workspace not eligible | `Workspace {WorkspaceId} ({Status}) for {UserEmail} — skipping, not eligible` | Debug |
| Cleanup triggered | `Cleaning up workspace {WorkspaceId} for {UserEmail} ({Status}) — reason: {Reason}` | Info |
| Stack deleted | `Deleting CloudFormation stack {StackName} for workspace {WorkspaceId}` | Info |
| Record removed | `Removed workspace record {WorkspaceId} for {UserEmail}` | Info |
| Cleanup failed | `Failed to clean up workspace {WorkspaceId} ({Status}) for {UserEmail} — reason: {Reason}` | Error |
| Record re-provisioned (race) | `Workspace {WorkspaceId} for {UserEmail} was re-provisioned — skipping DynamoDB delete` | Info |
| Scan complete | `Cleanup scan complete — scanned: {Scanned}, skipped: {Skipped}, cleaned: {Cleaned}, errors: {Errors}` | Info |
| High scan volume | `Scanned {Count} workspace records — consider adding a status-indexed GSI` | Warning |

### 6.2 AWS Console Paths

| Surface | Path | What to Check |
|---------|------|---------------|
| **CloudFormation** | Stacks → `workspace-{workspaceId}` | Stack status, events, outputs |
| **ECS** | Clusters → `workspace-cluster` → Services → `workspace-{workspaceId}` | Service status, task status, events |
| **EC2** | Load Balancers → `{workspaceId}-alb` | Listener rules, target group health |
| **DynamoDB** | Tables → `workspace-lookup-prod` → Explore items | Workspace record status and metadata |
| **Route 53** | Hosted zones → `{domain}` | DNS record for `{workspaceId}.{domain}` |
| **CloudWatch** | Log groups → `/ecs/workspace-{workspaceId}` | Coder container stdout/stderr |
| **CloudWatch** | Log groups → ProvisioningService | API-level lifecycle events |
| **CloudWatch** | Log groups → CleanupService | Cleanup scan results |

### 6.3 DynamoDB Record Schema

```
PK:             USER#{email}
SK:             WORKSPACE
EntityType:     Workspace
WorkspaceId:    a1b2c3d4e5f6
UserEmail:      user@example.com
StackId:        arn:aws:cloudformation:us-east-1:123456789:stack/workspace-a1b2c3d4e5f6/...
StackName:      workspace-a1b2c3d4e5f6
Status:         active | provisioning | failed | deleting
WorkspaceUrl:   https://a1b2c3d4e5f6.example.com  (when active)
AlbDnsName:     a1b2c3d4e5f6-alb-123456789.us-east-1.elb.amazonaws.com  (when active)
EcsServiceName: workspace-a1b2c3d4e5f6  (when active)
GSI1PK:         WORKSPACE#a1b2c3d4e5f6   (for workspace ID lookup)
GSI1SK:         USER#user@example.com    (for workspace ID lookup)
CreatedAt:      2026-01-15T10:30:00.0000000Z
UpdatedAt:      2026-01-15T10:35:00.0000000Z
```

---

## 7. Quick Reference

### 7.1 "Is this workspace alive?"

```bash
# One-shot check
WORKSPACE_ID="a1b2c3d4e5f6"

echo "--- DynamoDB ---"
aws dynamodb query --table-name workspace-lookup-prod --index-name GSI1 \
  --key-condition-expression 'GSI1PK = :pk' \
  --expression-attribute-values '{":pk":{"S":"WORKSPACE#'${WORKSPACE_ID}'"}}' \
  --query 'Items[0].[Status,WorkspaceUrl,StackName]'

echo "--- CloudFormation ---"
aws cloudformation describe-stacks --stack-name workspace-${WORKSPACE_ID} \
  --query 'Stacks[0].[StackStatus,Outputs]' 2>&1 || echo "Stack not found"

echo "--- ECS ---"
aws ecs describe-services --cluster workspace-cluster \
  --services workspace-${WORKSPACE_ID} \
  --query 'services[0].[status,runningCount,desiredCount]' 2>&1 || echo "Service not found"
```

### 7.2 "How many workspaces exist right now?"

```bash
aws dynamodb scan --table-name workspace-lookup-prod \
  --query 'Items[*].[Status.S,UserEmail.S]' --output table
```

### 7.3 "What's failing in provisioning across all workspaces?"

```bash
aws cloudformation describe-stacks \
  --query "Stacks[?starts_with(StackName,'workspace-') && contains(['CREATE_FAILED','ROLLBACK_COMPLETE','ROLLBACK_IN_PROGRESS','DELETE_FAILED'],StackStatus)].[StackName,StackStatus,StackStatusReason]" \
  --output table
```

---

## Related Documentation

- [docs/deployment-workflow.md](deployment-workflow.md) — Deployment process and normal lifecycle
- [docs/shared-platform-boundary.md](shared-platform-boundary.md) — Shared vs. per-workspace resource ownership
- [HLD.md](../HLD.md) — Architectural decisions
- [TECHNICAL.md](../TECHNICAL.md) — Technical implementation details
