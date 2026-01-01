variable "environment" {
  description = "Deployment environment (prod, staging, sandbox)"
  type        = string

  validation {
    condition     = contains(["prod", "staging", "sandbox"], var.environment)
    error_message = "Environment must be one of: prod, staging, sandbox"
  }
}

variable "vpc_cidr" {
  description = "CIDR block for the shared VPC"
  type        = string
  default     = "10.0.0.0/16"
}

variable "public_subnet_cidrs" {
  description = "CIDR blocks for public subnets (one per AZ)"
  type        = list(string)
  default     = ["10.0.1.0/24", "10.0.2.0/24"]
}

variable "private_subnet_cidrs" {
  description = "CIDR blocks for private subnets (one per AZ)"
  type        = list(string)
  default     = ["10.0.3.0/24", "10.0.4.0/24"]
}

variable "availability_zones" {
  description = "AZs to deploy into (defaults to first 2 available)"
  type        = list(string)
  default     = []
}

variable "enable_nat_gateway" {
  description = "Provision a NAT Gateway for private subnet egress. Cost note: ~$32/month + data transfer. Prefer VPC endpoints for production cost optimization."
  type        = bool
  default     = true
}

variable "common_tags" {
  description = "Tags applied to all resources"
  type        = map(string)
  default     = {}
}
