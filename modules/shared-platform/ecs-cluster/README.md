# Shared Platform ECS Cluster Module

## Purpose

Provisions the shared ECS cluster that hosts all per-workspace ECS services. Workspace stacks deploy services into this cluster but each workspace gets its own isolated service.

## Usage

```hcl
module "ecs" {
  source = "../../../modules/shared-platform/ecs-cluster"

  environment  = "prod"
  cluster_name = "workspace-cluster"
  common_tags  = local.common_tags
}
```

## Resources Created

| Resource | Purpose | Shared? |
|----------|---------|---------|
| ECS Cluster | Hosting boundary for workspace services | Shared |
| Fargate capacity providers | FARGATE + FARGATE_SPOT | Shared |
| Task execution IAM role | Pull images, write CloudWatch logs | Shared |
| Task IAM role | Application-level permissions | Shared |
| CloudWatch log group | Cluster-level log group | Shared |

## Outputs

| Output | Consumed By |
|--------|------------|
| `cluster_name` | Workspace CloudFormation stacks (ECS service placement) |
| `cluster_arn` | IAM policies, CF references |
| `task_execution_role_arn` | Workspace task definitions |
| `task_role_arn` | Workspace task definitions |
| `log_group_name` | Workspace logging config |

## Design Notes

- **Shared cluster, isolated services:** Each workspace gets its own ECS service in this cluster. The cluster provides the hosting boundary; services provide isolation.
- **Fargate launch type:** Pay-per-task, zero-cost-when-idle. Only costs incurred while workspace tasks are running.
- **Container Insights enabled** for operational visibility.
