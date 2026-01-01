output "vpc_id" {
  description = "VPC ID for workspace stack resources"
  value       = aws_vpc.main.id
}

output "public_subnet_ids" {
  description = "Public subnet IDs for workspace ALBs"
  value       = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  description = "Private subnet IDs for workspace ECS tasks"
  value       = aws_subnet.private[*].id
}

output "ecs_tasks_security_group_id" {
  description = "Security group ID for workspace ECS tasks"
  value       = aws_security_group.ecs_tasks.id
}

output "alb_security_group_id" {
  description = "Security group ID for workspace ALBs"
  value       = aws_security_group.alb.id
}

output "availability_zones" {
  description = "Availability zones in use"
  value       = local.azs
}

output "vpc_cidr" {
  description = "VPC CIDR block for workspace egress scoping"
  value       = aws_vpc.main.cidr_block
}
