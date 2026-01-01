# Shared Platform Cognito Module

## Purpose

Provisions the shared Cognito User Pool that authenticates users before they reach workspace infrastructure. Used by both the frontend portal and workspace ALB auth.

## Usage

```hcl
module "cognito" {
  source = "../../../modules/shared-platform/cognito"

  environment   = "prod"
  domain_prefix = "platform-workspaces-prod"
  callback_urls = ["https://portal.example.com/callback"]
  logout_urls   = ["https://portal.example.com"]
  common_tags   = local.common_tags
}
```

## Resources Created

| Resource | Purpose | Shared? |
|----------|---------|---------|
| Cognito User Pool | Identity store for platform users | Shared |
| User Pool Domain | Hosted UI domain for sign-in | Shared |
| User Pool Client (portal) | OAuth client for the frontend portal | Shared |

## Outputs

| Output | Consumed By |
|--------|------------|
| `user_pool_id` | Workspace ALB Cognito auth config, frontend |
| `user_pool_arn` | Workspace ALB listener rules, IAM policies |
| `user_pool_domain` | Frontend sign-in redirect |
| `portal_client_id` | Frontend OAuth configuration |

## Design Notes

- **Email as username:** Users sign in with email address, not a generated username.
- **OAuth PKCE:** Authorization code grant with PKCE (no client secret needed).
- **Per-workspace clients:** Each workspace stack can create additional app clients for ALB auth if needed, or the shared portal client can be reused.
