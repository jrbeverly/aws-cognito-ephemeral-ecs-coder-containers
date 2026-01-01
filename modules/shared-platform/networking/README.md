# Shared Platform Networking Module

## Purpose

Provisions the shared VPC, subnets, route tables, NAT gateway, and security groups that workspace deployments attach to.

## Usage

```hcl
module "networking" {
  source = "../../../modules/shared-platform/networking"

  environment = "prod"
  common_tags = local.common_tags
}
```

## Resources Created

| Resource | Purpose | Shared? |
|----------|---------|---------|
| VPC | Network boundary for all platform resources | Shared |
| Public subnets (2) | ALB placement for workspace endpoints | Shared |
| Private subnets (2) | ECS task placement for workspace runtimes | Shared |
| Internet Gateway | Public subnet egress | Shared |
| NAT Gateway | Private subnet egress (cost: ~$32/mo) | Shared |
| ECS tasks security group | Default outbound-only SG for workspace tasks | Shared |
| ALB security group | Default HTTPS/HTTP ingress SG for workspace ALBs | Shared |

## Outputs

| Output | Consumed By |
|--------|------------|
| `vpc_id` | Workspace CloudFormation stacks |
| `public_subnet_ids` | Workspace ALBs |
| `private_subnet_ids` | Workspace ECS services |
| `ecs_tasks_security_group_id` | Workspace task definitions |
| `alb_security_group_id` | Workspace ALBs |

## Cost Note

The NAT Gateway costs ~$32/month regardless of traffic. For production cost optimization, consider replacing it with VPC endpoints (ECR, S3, CloudWatch Logs) to eliminate the fixed cost.
