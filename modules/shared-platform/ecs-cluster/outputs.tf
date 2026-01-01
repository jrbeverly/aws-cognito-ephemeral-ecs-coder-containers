output "cluster_name" {
  description = "ECS cluster name for workspace service placement"
  value       = aws_ecs_cluster.main.name
}

output "cluster_arn" {
  description = "ECS cluster ARN for IAM policies and CF references"
  value       = aws_ecs_cluster.main.arn
}

output "task_execution_role_arn" {
  description = "IAM role ARN for ECS task execution (pulling images, writing logs)"
  value       = aws_iam_role.ecs_task_execution.arn
}

output "task_execution_role_name" {
  description = "IAM role name for ECS task execution"
  value       = aws_iam_role.ecs_task_execution.name
}

output "task_role_arn" {
  description = "IAM role ARN for workspace ECS tasks (application-level permissions)"
  value       = aws_iam_role.ecs_task.arn
}

output "task_role_name" {
  description = "IAM role name for workspace ECS tasks"
  value       = aws_iam_role.ecs_task.name
}

output "log_group_name" {
  description = "CloudWatch log group for cluster-level logging"
  value       = aws_cloudwatch_log_group.cluster.name
}
