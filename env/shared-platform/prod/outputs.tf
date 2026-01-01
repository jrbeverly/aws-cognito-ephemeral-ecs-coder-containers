# ── Workspace CloudFormation Stack Inputs ──
# Workspace stacks consume these outputs. Do not change output names without
# updating workspace stack template parameter names to match.

# ── Networking ──
output "vpc_id" {
  description = "VPC ID for workspace ALBs and ECS services"
  value       = module.networking.vpc_id
}

output "public_subnet_ids" {
  description = "Public subnet IDs for workspace ALBs"
  value       = module.networking.public_subnet_ids
}

output "private_subnet_ids" {
  description = "Private subnet IDs for workspace ECS tasks"
  value       = module.networking.private_subnet_ids
}

output "ecs_tasks_security_group_id" {
  description = "Security group ID for workspace ECS tasks"
  value       = module.networking.ecs_tasks_security_group_id
}

output "alb_security_group_id" {
  description = "Security group ID for workspace ALBs"
  value       = module.networking.alb_security_group_id
}

# ── ECS ──
output "ecs_cluster_name" {
  description = "ECS cluster name for workspace service placement"
  value       = module.ecs.cluster_name
}

output "ecs_cluster_arn" {
  description = "ECS cluster ARN for CF references"
  value       = module.ecs.cluster_arn
}

output "ecs_task_execution_role_arn" {
  description = "IAM role ARN for ECS task execution"
  value       = module.ecs.task_execution_role_arn
}

output "ecs_task_role_arn" {
  description = "IAM role ARN for workspace ECS tasks"
  value       = module.ecs.task_role_arn
}

output "ecs_task_role_name" {
  description = "IAM role name for workspace ECS tasks"
  value       = module.ecs.task_role_name
}

# ── Identity ──
output "cognito_user_pool_id" {
  description = "Cognito User Pool ID for ALB auth"
  value       = module.cognito.user_pool_id
}

output "cognito_user_pool_arn" {
  description = "Cognito User Pool ARN for ALB listener rules and IAM"
  value       = module.cognito.user_pool_arn
}

output "cognito_user_pool_domain" {
  description = "Cognito hosted UI domain for sign-in"
  value       = module.cognito.user_pool_domain
}

output "cognito_domain_prefix" {
  description = "Cognito hosted UI domain prefix (for workspace ALB auth)"
  value       = module.cognito.cognito_domain_prefix
}

output "cognito_portal_client_id" {
  description = "Cognito app client ID for the frontend portal"
  value       = module.cognito.portal_client_id
}

# ── DNS & TLS ──
output "hosted_zone_id" {
  description = "Route 53 hosted zone ID for workspace DNS records"
  value       = data.aws_route53_zone.main.zone_id
}

output "domain_name" {
  description = "Root domain name for workspace subdomain construction"
  value       = local.domain_name
}

output "certificate_arn" {
  description = "ACM certificate ARN for workspace ALB HTTPS listeners"
  value       = aws_acm_certificate.workspaces.arn
}

output "vpc_cidr" {
  description = "VPC CIDR block for workspace egress scoping"
  value       = module.networking.vpc_cidr
}

# ── Workspace Lookup ──
output "workspace_lookup_table_name" {
  description = "DynamoDB table name for workspace lookup metadata"
  value       = aws_dynamodb_table.workspace_lookup.name
}

output "workspace_lookup_table_arn" {
  description = "DynamoDB table ARN for IAM policies"
  value       = aws_dynamodb_table.workspace_lookup.arn
}

# ── Control plane ──
output "portal_url" {
  description = "Portal + Provisioning API URL"
  value       = "https://${local.domain_name}"
}

output "control_plane_repository_url" {
  description = "ECR repository for the control-plane image"
  value       = aws_ecr_repository.control_plane.repository_url
}

output "workspace_runtime_repository_url" {
  description = "ECR repository for the workspace runtime image"
  value       = aws_ecr_repository.workspace_runtime.repository_url
}

output "control_plane_service_name" {
  description = "ECS service running the portal + Provisioning API"
  value       = aws_ecs_service.control_plane.name
}
