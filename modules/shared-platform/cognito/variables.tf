variable "environment" {
  description = "Deployment environment"
  type        = string
}

variable "pool_name" {
  description = "Cognito user pool name"
  type        = string
  default     = "platform-users"
}

variable "domain_prefix" {
  description = "Cognito hosted UI domain prefix (globally unique)"
  type        = string
}

variable "callback_urls" {
  description = "Allowed callback URLs (frontend portal + workspace endpoints)"
  type        = list(string)
  default     = []
}

variable "logout_urls" {
  description = "Allowed sign-out URLs"
  type        = list(string)
  default     = []
}

variable "common_tags" {
  description = "Tags applied to all resources"
  type        = map(string)
  default     = {}
}
