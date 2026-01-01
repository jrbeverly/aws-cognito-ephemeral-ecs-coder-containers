output "user_pool_id" {
  description = "Cognito User Pool ID for ALB auth configuration"
  value       = aws_cognito_user_pool.main.id
}

output "user_pool_arn" {
  description = "Cognito User Pool ARN for IAM policies and ALB auth"
  value       = aws_cognito_user_pool.main.arn
}

output "user_pool_endpoint" {
  description = "Cognito User Pool endpoint URL"
  value       = "https://cognito-idp.${data.aws_region.current.id}.amazonaws.com/${aws_cognito_user_pool.main.id}"
}

output "user_pool_domain" {
  description = "Cognito hosted UI domain for sign-in"
  value       = "${aws_cognito_user_pool_domain.main.domain}.auth.${data.aws_region.current.id}.amazoncognito.com"
}

output "cognito_domain_prefix" {
  description = "Cognito hosted UI domain prefix (for ALB auth config)"
  value       = aws_cognito_user_pool_domain.main.domain
}

output "portal_client_id" {
  description = "App client ID for the frontend portal"
  value       = aws_cognito_user_pool_client.portal.id
}

output "portal_client_secret" {
  description = "App client secret (if generate_secret is enabled)"
  value       = try(aws_cognito_user_pool_client.portal.client_secret, null)
  sensitive   = true
}

data "aws_region" "current" {}
