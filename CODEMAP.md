# CODEMAP

Repository structure, technology stack, and implementation patterns for `aws-cognito-ephemeral-ecs-coder-containers`.

## Repository Layout

```
/
├── src/                     # Backend services (C# /.NET)
│   └── {ServiceName}/
│       └── {ServiceName}.Api/
├── app/                     # Frontend applications (Vue.js / TypeScript)
│   └── {ServiceName}/
├── env/                     # Deployable infrastructure (Terraform)
│   └── {service-name}/
│       └── {environment}/
├── modules/                 # Reusable Terraform modules (project-local)
│   └── shared-platform/     # Shared platform infrastructure modules
│       ├── networking/
│       ├── ecs-cluster/
│       └── cognito/
├── tests/                   # Tests (mirrors source structure)
│   └── {ServiceName}/
│       └── {ServiceName}.Tests/
├── docs/                    # Human documentation
└── docs/shared-platform-boundary.md
```

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| **Backend API** | C# / .NET Minimal API | .NET 10 |
| **Frontend** | Vue.js 3 + TypeScript + Vuetify | Vue 3.4+, TS 5.3+ |
| **Infrastructure (shared)** | Terraform | 1.6+ |
| **Workspace deployments** | CloudFormation | per-workspace stacks |
| **Runtime hosting** | AWS ECS (Fargate) | shared cluster |
| **Identity** | AWS Cognito | User Pool |
| **Database** | DynamoDB | on-demand billing |
| **DNS & TLS** | Route 53 + ACM | |
| **CI/CD** | Gitea Actions | `.gitea/workflows/` |

## Services

The platform consists of these logical services:

| Service | Directory Pattern | Description |
|---------|------------------|-------------|
| **Portal Frontend** | `app/PortalService/` | User-facing web app for auth, workspace lookup, reconnect |
| **Provisioning API** | `src/ProvisioningService/` | Thin API layer for workspace lifecycle operations |
| **Cleanup Service** | `src/CleanupService/` | Scheduled worker for workspace expiration and teardown |
| **Shared Platform** | `env/shared-platform/` | VPC, ECS cluster, Cognito — provisioned once per environment |
| **Workspace Stack** | CloudFormation (per-user) | ALB, ECS service, Coder container, storage — one per user |

## Conventions

### Naming

- **PascalCase** for backend service directories: `src/PortalService/`
- **kebab-case** for infrastructure and modules: `env/shared-platform/`, `modules/shared-platform/`
- **kebab-case** for Terraform resources: `workspace-cluster`

### File Organization

- **Backend:** One endpoint per file (`src/{Service}/{Service}.Api/Endpoints/{Resource}/v{N}/{Action}Endpoint.cs`)
- **Frontend:** Service-scoped under `app/{ServiceName}/src/`
- **Infrastructure:** Environment-scoped under `env/{service}/{environment}/`
- **Modules:** Function-scoped under `modules/{domain}/{module-name}/`

### Tags

All resources must carry at minimum:

| Tag | Value |
|-----|-------|
| `Environment` | `prod`, `staging`, `sandbox` |
| `Service` | Service name |
| `ManagedBy` | `Terraform` or `CloudFormation` |
| `Owner` | Owner email |
| `CostCenter` | Billing category |

## Build & Test

```bash
# Backend
dotnet build src/{ServiceName}/{ServiceName}.Api/
dotnet test tests/{ServiceName}/

# Frontend
cd app/{ServiceName} && npm ci && npm run build && npm run test

# Full deployment (Terraform + images + control plane + Cognito user)
make deploy AUTO_APPROVE=1 USER_EMAIL=you@example.com
```

## Key Documentation

| Document | Purpose |
|----------|---------|
| [PROBLEM.md](PROBLEM.md) | Problem statement and governance requirements |
| [VISION.md](VISION.md) | Product vision and user experience goals |
| [HLD.md](HLD.md) | High-level architectural design and locked decisions |
| [TECHNICAL.md](TECHNICAL.md) | Technical implementation guidance |
| [docs/shared-platform-boundary.md](docs/shared-platform-boundary.md) | Shared vs. per-workspace resource boundary |

## Architecture Decisions

See [HLD.md](HLD.md) for locked architectural decisions. The key ones:

1. Per-user workspace = one CloudFormation deployment unit
2. ALB + Cognito at each workspace edge (not shared routing mesh)
3. ECS Fargate for workspace runtime hosting in a shared cluster
4. CloudFormation as source of truth for deployed workspace state
5. One active workspace per user at a time
6. Cleanup via scheduled stack deletion
