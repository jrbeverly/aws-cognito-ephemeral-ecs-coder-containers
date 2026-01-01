variable "environment" {
  description = "Deployment environment"
  type        = string
  default     = "prod"
}

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "ca-central-1"
}

variable "hosted_zone_name" {
  description = "Existing public Route 53 hosted zone; the platform is served at coder.<zone> and workspaces at <id>.coder.<zone>"
  type        = string
}

variable "cluster_name" {
  description = "ECS cluster name"
  type        = string
  default     = "workspace-cluster"
}

variable "log_retention_days" {
  description = "CloudWatch log retention in days"
  type        = number
  default     = 7
}

variable "owner_email" {
  description = "Email of platform owner for tagging"
  type        = string
  default     = ""
}

variable "cost_center" {
  description = "Cost center for billing"
  type        = string
  default     = "engineering"
}

variable "image_tag" {
  description = "Tag of the control-plane and workspace-runtime images in ECR"
  type        = string
  default     = "latest"
}
