# Per-user browser VS Code on ECS Fargate behind Cognito

Can a per-user CloudFormation stack give one authenticated user a browser VS
Code workspace on ECS Fargate, behind an ALB that performs Cognito login, with
the workspace's home directory surviving a task replacement and the whole
stack deleting itself when it expires?

This is the Fargate counterpart to an EC2-based variant. The portal, the
provisioning API and the "does this user already have a workspace" lookup are
already covered there and are out of scope here. A user's workspace is created
and deleted with a script.

The experiment should:

1. keep one shared stack (VPC, ECS cluster, Cognito user pool, ACM wildcard
   certificate, one ALB with an HTTPS listener) deployed once;
2. create a per-user stack from one CloudFormation template taking the user
   name as its only parameter: an EFS access point, a Fargate service running
   `code-server` (VS Code in the browser) with `/home/coder` on EFS, a target
   group and a host-header listener rule `<user>.<domain>` whose first action
   is `authenticate-cognito`;
3. give each per-user stack an `ExpiresAt` parameter and a one-time EventBridge
   Scheduler schedule inside the same stack that calls CloudFormation
   `DeleteStack` on itself directly, with no Lambda;
4. use one script to create, open, stop the task, reopen, and delete.

Observations to record:

- the browser reaches VS Code only after the Cognito login, and a second user
  cannot open the first user's host;
- a file written in `/home/coder` is still present after `aws ecs stop-task`
  forces a replacement task;
- time from stack create to a usable editor, and time to recover after a task
  stop;
- VS Code's WebSocket traffic works through the ALB with no extra settings;
- the self-deleting schedule removes an expired stack, including its EFS
  access point, and whether the schedule deleting its own stack causes any
  ordering problem.

Constraints: one region, hardcoded (`ca-central-1`); an existing Route 53
hosted zone passed as a parameter; the public `codercom/code-server` image;
hardcoded user names for testing; the ALB Cognito app client must have a
client secret.

Out of scope unless results show a need: the Coder control plane and its
templates, a portal or API, a DynamoDB lookup table, image build pipelines,
per-user IAM beyond the task role, private networking without NAT, VPC
endpoints, observability, and reusable Terraform modules.
