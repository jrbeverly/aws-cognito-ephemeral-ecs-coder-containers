# Shared Platform Boundary

## Purpose

Define the boundary between shared platform resources and per-workspace resources. This document is the authoritative reference for what belongs in the shared foundation vs. what belongs in individual workspace stacks.

## Principle

**Shared platform resources are provisioned once by Terraform. Workspace resources are provisioned per-user by CloudFormation.**

The shared platform exists to provide the minimum set of prerequisites that workspace stacks need to operate cleanly. It does not own, route, or manage individual workspaces.

## Shared Platform Resources (Terraform)

These resources exist once per environment and are managed in `env/shared-platform/{environment}/`.

| Layer | Resources | Module |
|-------|-----------|--------|
| **Networking** | VPC, public subnets, private subnets, Internet Gateway, NAT Gateway, route tables, base security groups | `modules/shared-platform/networking` |
| **Hosting** | ECS cluster, Fargate capacity providers, task execution/task IAM roles, CloudWatch log group | `modules/shared-platform/ecs-cluster` |
| **Identity** | Cognito User Pool, User Pool Domain, portal App Client | `modules/shared-platform/cognito` |
| **DNS & TLS** | ACM wildcard certificate, Route 53 hosted zone reference | Managed in env config |
| **Workspace Lookup** | DynamoDB table for workspace metadata (derived index — CloudFormation is authoritative) | Managed in env config |

## Per-Workspace Resources (CloudFormation)

Each user workspace stack owns its own resources managed by CloudFormation. Shared outputs are passed as stack parameters.

| Layer | Resources | Lifecycle |
|-------|-----------|-----------|
| **Load Balancer** | ALB with Cognito authentication, HTTPS listener, target group | Created with workspace, destroyed with workspace |
| **DNS Record** | Route 53 A/AAAA record pointing to workspace ALB | Created with workspace, destroyed with workspace |
| **ECS Service** | ECS service in shared cluster, task definition, container with Coder | Created with workspace, destroyed with workspace |
| **Storage** | EFS file system or EBS volume attached to workspace task | Created with workspace, destroyed with workspace |
| **Security Group** | Workspace-specific SG (attached to shared ALB SG) | Created with workspace, destroyed with workspace |
| **IAM** | Workspace-scoped IAM policies for approved internal services | Created with workspace, destroyed with workspace |

## Outputs Consumed by Workspace Stacks

Workspace CloudFormation stacks receive these shared outputs as stack parameters:

| Parameter | Source | Purpose |
|-----------|--------|---------|
| `VpcId` | `module.networking.vpc_id` | Place ALB and ECS resources |
| `PublicSubnetIds` | `module.networking.public_subnet_ids` | ALB subnets |
| `PrivateSubnetIds` | `module.networking.private_subnet_ids` | ECS task subnets |
| `EcsTasksSecurityGroupId` | `module.networking.ecs_tasks_security_group_id` | Task networking |
| `AlbSecurityGroupId` | `module.networking.alb_security_group_id` | ALB networking |
| `EcsClusterName` | `module.ecs.cluster_name` | ECS service placement |
| `EcsTaskExecutionRoleArn` | `module.ecs.task_execution_role_arn` | Task execution |
| `EcsTaskRoleArn` | `module.ecs.task_role_arn` | Task application permissions |
| `CognitoUserPoolId` | `module.cognito.user_pool_id` | ALB auth config |
| `CognitoUserPoolArn` | `module.cognito.user_pool_arn` | ALB listener rules |
| `HostedZoneId` | `data.aws_route53_zone.main.zone_id` | Workspace DNS records |
| `DomainName` | `var.domain_name` | Subdomain construction |
| `CertificateArn` | `aws_acm_certificate.workspaces.arn` | ALB HTTPS |

## Explicit Non-Goals

The shared platform layer does not:

- Provide per-workspace load balancers — each workspace deploys its own ALB.
- Implement a shared multi-tenant routing mesh — each workspace gets its own DNS record and endpoint.
- Manage workspace lifecycle — CloudFormation stacks own workspace lifecycle; cleanup is a separate scheduled process.
- Pre-configure workspace containers or Coder — the shared layer provides hosting and identity; workspace stacks define the application runtime.

## Adding New Shared Resources

When a new prerequisite is needed by workspace stacks:

1. Determine if it's truly shared (needed by all workspaces) or per-workspace.
2. Add it to the appropriate shared module or create a new module in `modules/shared-platform/`.
3. Expose it as an output in `env/shared-platform/{environment}/outputs.tf`.
4. Update this document.
5. Update workspace CloudFormation template parameters to consume the new output.

Shared resources should remain minimal. When in doubt, keep resources in workspace stacks.
