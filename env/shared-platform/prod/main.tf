locals {
  common_tags = {
    Environment = var.environment
    Service     = "shared-platform"
    ManagedBy   = "Terraform"
    Owner       = var.owner_email
    CostCenter  = var.cost_center
  }

  domain_name = "coder.${var.hosted_zone_name}"
}

data "aws_caller_identity" "current" {}

module "networking" {
  source = "../../../modules/shared-platform/networking"

  environment = var.environment
  common_tags = local.common_tags
}

module "ecs" {
  source = "../../../modules/shared-platform/ecs-cluster"

  environment        = var.environment
  cluster_name       = var.cluster_name
  log_retention_days = var.log_retention_days
  common_tags        = local.common_tags
}

module "cognito" {
  source = "../../../modules/shared-platform/cognito"

  environment   = var.environment
  domain_prefix = "coder-workspaces-${data.aws_caller_identity.current.account_id}"
  callback_urls = ["https://${local.domain_name}/callback"]
  logout_urls   = ["https://${local.domain_name}"]
  common_tags   = local.common_tags
}

# ACM certificate for workspace ALBs (wildcard for per-workspace subdomains)
resource "aws_acm_certificate" "workspaces" {
  domain_name               = local.domain_name
  subject_alternative_names = ["*.${local.domain_name}"]
  validation_method         = "DNS"

  tags = merge(local.common_tags, {
    Component = "Certificate"
  })

  lifecycle {
    create_before_destroy = true
  }
}

# Route 53 hosted zone (assumes domain is managed in Route 53)
data "aws_route53_zone" "main" {
  name         = var.hosted_zone_name
  private_zone = false
}

# DNS validation records for ACM certificate
resource "aws_route53_record" "cert_validation" {
  for_each = {
    for dvo in aws_acm_certificate.workspaces.domain_validation_options : dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  }

  allow_overwrite = true
  name            = each.value.name
  records         = [each.value.record]
  ttl             = 60
  type            = each.value.type
  zone_id         = data.aws_route53_zone.main.zone_id
}

resource "aws_acm_certificate_validation" "workspaces" {
  certificate_arn         = aws_acm_certificate.workspaces.arn
  validation_record_fqdns = [for record in aws_route53_record.cert_validation : record.fqdn]
}

# Workspace lookup DynamoDB table (derived index for portal UX, cleanup, and observability)
# CloudFormation remains the authoritative source of truth for deployed infrastructure.
resource "aws_dynamodb_table" "workspace_lookup" {
  name         = "workspace-lookup-${var.environment}"
  billing_mode = "PAY_PER_REQUEST"

  hash_key  = "PK"
  range_key = "SK"

  attribute {
    name = "PK"
    type = "S"
  }

  attribute {
    name = "SK"
    type = "S"
  }

  attribute {
    name = "GSI1PK"
    type = "S"
  }

  attribute {
    name = "GSI1SK"
    type = "S"
  }

  global_secondary_index {
    name            = "GSI1"
    hash_key        = "GSI1PK"
    range_key       = "GSI1SK"
    projection_type = "ALL"
  }

  tags = merge(local.common_tags, {
    Component = "Database"
    Name      = "workspace-lookup-${var.environment}"
  })
}
