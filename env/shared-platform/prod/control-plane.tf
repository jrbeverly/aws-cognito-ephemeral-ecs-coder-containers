# Control plane: the portal + Provisioning API as one Fargate service at
# https://coder.<zone>, the cleanup worker as a scheduled Fargate task, and the
# artifacts both depend on (ECR images, workspace template in S3).

locals {
  account_id = data.aws_caller_identity.current.account_id

  control_plane_image     = "${aws_ecr_repository.control_plane.repository_url}:${var.image_tag}"
  workspace_runtime_image = "${aws_ecr_repository.workspace_runtime.repository_url}:${var.image_tag}"

  control_plane_env = [
    { name = "AWS_REGION", value = var.aws_region },
    { name = "DynamoDb__TableName", value = aws_dynamodb_table.workspace_lookup.name },
    { name = "Cognito__Authority", value = "https://cognito-idp.${var.aws_region}.amazonaws.com/${module.cognito.user_pool_id}" },
    { name = "Cognito__ClientId", value = module.cognito.portal_client_id },
    { name = "WorkspaceStack__TemplateUrl", value = "https://${aws_s3_bucket.templates.bucket_regional_domain_name}/${aws_s3_object.workspace_template.key}" },
    { name = "WorkspaceStack__CloudFormationRoleArn", value = aws_iam_role.cloudformation.arn },
    { name = "WorkspaceStack__VpcId", value = module.networking.vpc_id },
    { name = "WorkspaceStack__VpcCidr", value = module.networking.vpc_cidr },
    { name = "WorkspaceStack__PublicSubnetIds", value = join(",", module.networking.public_subnet_ids) },
    { name = "WorkspaceStack__PrivateSubnetIds", value = join(",", module.networking.private_subnet_ids) },
    { name = "WorkspaceStack__EcsClusterName", value = module.ecs.cluster_name },
    { name = "WorkspaceStack__EcsTaskExecutionRoleArn", value = module.ecs.task_execution_role_arn },
    { name = "WorkspaceStack__EcsTaskRoleArn", value = module.ecs.task_role_arn },
    { name = "WorkspaceStack__AlbSecurityGroupId", value = module.networking.alb_security_group_id },
    { name = "WorkspaceStack__CognitoUserPoolId", value = module.cognito.user_pool_id },
    { name = "WorkspaceStack__CognitoUserPoolArn", value = module.cognito.user_pool_arn },
    { name = "WorkspaceStack__CognitoDomainPrefix", value = module.cognito.cognito_domain_prefix },
    { name = "WorkspaceStack__HostedZoneId", value = data.aws_route53_zone.main.zone_id },
    { name = "WorkspaceStack__DomainName", value = local.domain_name },
    { name = "WorkspaceStack__CertificateArn", value = aws_acm_certificate_validation.workspaces.certificate_arn },
    { name = "WorkspaceStack__ContainerImage", value = local.workspace_runtime_image },
    { name = "WorkspaceStack__Environment", value = var.environment },
  ]
}

# ── Images ──

resource "aws_ecr_repository" "control_plane" {
  name         = "coder-control-plane-${var.environment}"
  force_delete = true
  tags         = local.common_tags
}

resource "aws_ecr_repository" "workspace_runtime" {
  name         = "coder-workspace-runtime-${var.environment}"
  force_delete = true
  tags         = local.common_tags
}

# ── Workspace template ──

resource "aws_s3_bucket" "templates" {
  bucket        = "coder-workspace-templates-${local.account_id}-${var.aws_region}"
  force_destroy = true
  tags          = local.common_tags
}

resource "aws_s3_object" "workspace_template" {
  bucket = aws_s3_bucket.templates.id
  key    = "workspace/template.yaml"
  source = "${path.module}/../../workspace/template.yaml"
  etag   = filemd5("${path.module}/../../workspace/template.yaml")
}

# CloudFormation assumes this role to create and delete workspace stacks, so
# the control plane itself only needs CloudFormation + PassRole.
resource "aws_iam_role" "cloudformation" {
  name = "coder-workspace-cloudformation-${var.environment}"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "cloudformation.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })

  tags = local.common_tags
}

resource "aws_iam_role_policy" "cloudformation" {
  name = "workspace-stack-resources"
  role = aws_iam_role.cloudformation.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "ec2:*",
          "elasticloadbalancing:*",
          "ecs:*",
          "elasticfilesystem:*",
          "logs:*",
          "route53:*",
          "cognito-idp:*",
          "iam:CreateServiceLinkedRole",
        ]
        Resource = "*"
      },
      {
        Effect   = "Allow"
        Action   = "iam:*"
        Resource = "arn:aws:iam::${local.account_id}:role/workspace-*"
      },
      {
        Effect   = "Allow"
        Action   = "iam:PassRole"
        Resource = module.ecs.task_execution_role_arn
      },
    ]
  })
}

# ── Control plane service ──

resource "aws_cloudwatch_log_group" "control_plane" {
  name              = "/ecs/coder-control-plane-${var.environment}"
  retention_in_days = var.log_retention_days
  tags              = local.common_tags
}

resource "aws_iam_role" "control_plane" {
  name = "coder-control-plane-${var.environment}"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })

  tags = local.common_tags
}

resource "aws_iam_role_policy" "control_plane" {
  name = "control-plane"
  role = aws_iam_role.control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem",
          "dynamodb:PutItem",
          "dynamodb:UpdateItem",
          "dynamodb:DeleteItem",
          "dynamodb:Query",
          "dynamodb:Scan",
        ]
        Resource = [
          aws_dynamodb_table.workspace_lookup.arn,
          "${aws_dynamodb_table.workspace_lookup.arn}/index/*",
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "cloudformation:CreateStack",
          "cloudformation:DescribeStacks",
          "cloudformation:DeleteStack",
          "cloudformation:TagResource",
        ]
        Resource = "arn:aws:cloudformation:${var.aws_region}:${local.account_id}:stack/workspace-*/*"
      },
      {
        Effect   = "Allow"
        Action   = "iam:PassRole"
        Resource = aws_iam_role.cloudformation.arn
      },
      {
        Effect   = "Allow"
        Action   = "s3:GetObject"
        Resource = "${aws_s3_bucket.templates.arn}/*"
      },
    ]
  })
}

resource "aws_security_group" "control_plane" {
  name        = "coder-control-plane-${var.environment}"
  description = "Control plane tasks"
  vpc_id      = module.networking.vpc_id

  ingress {
    from_port       = 8080
    to_port         = 8080
    protocol        = "tcp"
    security_groups = [module.networking.alb_security_group_id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = local.common_tags
}

resource "aws_lb" "control_plane" {
  name               = "coder-control-plane-${var.environment}"
  load_balancer_type = "application"
  subnets            = module.networking.public_subnet_ids
  security_groups    = [module.networking.alb_security_group_id]
  tags               = local.common_tags
}

resource "aws_lb_target_group" "control_plane" {
  name        = "coder-control-plane-${var.environment}"
  port        = 8080
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = module.networking.vpc_id

  deregistration_delay = 10

  health_check {
    path    = "/"
    matcher = "200"
  }

  tags = local.common_tags
}

resource "aws_lb_listener" "control_plane_https" {
  load_balancer_arn = aws_lb.control_plane.arn
  port              = 443
  protocol          = "HTTPS"
  certificate_arn   = aws_acm_certificate_validation.workspaces.certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.control_plane.arn
  }
}

resource "aws_lb_listener" "control_plane_http" {
  load_balancer_arn = aws_lb.control_plane.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"
    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_route53_record" "control_plane" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = local.domain_name
  type    = "A"

  alias {
    name                   = aws_lb.control_plane.dns_name
    zone_id                = aws_lb.control_plane.zone_id
    evaluate_target_health = false
  }
}

resource "aws_ecs_task_definition" "control_plane" {
  family                   = "coder-control-plane-${var.environment}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = module.ecs.task_execution_role_arn
  task_role_arn            = aws_iam_role.control_plane.arn

  container_definitions = jsonencode([{
    name         = "api"
    image        = local.control_plane_image
    essential    = true
    portMappings = [{ containerPort = 8080, protocol = "tcp" }]
    environment  = local.control_plane_env
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.control_plane.name
        awslogs-region        = var.aws_region
        awslogs-stream-prefix = "api"
      }
    }
  }])

  tags = local.common_tags
}

resource "aws_ecs_service" "control_plane" {
  name            = "coder-control-plane"
  cluster         = module.ecs.cluster_arn
  task_definition = aws_ecs_task_definition.control_plane.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = module.networking.private_subnet_ids
    security_groups  = [aws_security_group.control_plane.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.control_plane.arn
    container_name   = "api"
    container_port   = 8080
  }

  depends_on = [aws_lb_listener.control_plane_https]

  tags = local.common_tags
}

# ── Cleanup worker (scheduled, run-to-completion) ──

resource "aws_ecs_task_definition" "cleanup" {
  family                   = "coder-cleanup-${var.environment}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = module.ecs.task_execution_role_arn
  task_role_arn            = aws_iam_role.control_plane.arn

  container_definitions = jsonencode([{
    name             = "cleanup"
    image            = local.control_plane_image
    essential        = true
    entryPoint       = ["dotnet", "/app/cleanup/CleanupService.Worker.dll"]
    workingDirectory = "/app/cleanup"
    environment = [
      { name = "AWS_REGION", value = var.aws_region },
      { name = "DynamoDb__TableName", value = aws_dynamodb_table.workspace_lookup.name },
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.control_plane.name
        awslogs-region        = var.aws_region
        awslogs-stream-prefix = "cleanup"
      }
    }
  }])

  tags = local.common_tags
}

resource "aws_iam_role" "scheduler" {
  name = "coder-cleanup-scheduler-${var.environment}"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "scheduler.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })

  tags = local.common_tags
}

resource "aws_iam_role_policy" "scheduler" {
  name = "run-cleanup"
  role = aws_iam_role.scheduler.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "ecs:RunTask"
        Resource = "arn:aws:ecs:${var.aws_region}:${local.account_id}:task-definition/${aws_ecs_task_definition.cleanup.family}:*"
      },
      {
        Effect   = "Allow"
        Action   = "iam:PassRole"
        Resource = [module.ecs.task_execution_role_arn, aws_iam_role.control_plane.arn]
      },
    ]
  })
}

resource "aws_scheduler_schedule" "cleanup" {
  name                = "coder-cleanup-${var.environment}"
  schedule_expression = "rate(15 minutes)"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = module.ecs.cluster_arn
    role_arn = aws_iam_role.scheduler.arn

    ecs_parameters {
      task_definition_arn = aws_ecs_task_definition.cleanup.arn
      launch_type         = "FARGATE"

      network_configuration {
        subnets          = module.networking.private_subnet_ids
        security_groups  = [aws_security_group.control_plane.id]
        assign_public_ip = false
      }
    }
  }
}
