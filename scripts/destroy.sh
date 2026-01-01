#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TF_DIR="${ROOT_DIR}/env/shared-platform/prod"
AWS_REGION="${AWS_REGION:-ca-central-1}"
AUTO_APPROVE="${AUTO_APPROVE:-0}"
export AWS_REGION AWS_DEFAULT_REGION="${AWS_REGION}"

log() { printf '[destroy] %s\n' "$*"; }

# Workspace stacks run under a Terraform-managed CloudFormation role, so they
# must be gone before Terraform removes it.
stacks="$(aws cloudformation list-stacks \
  --stack-status-filter CREATE_IN_PROGRESS CREATE_COMPLETE CREATE_FAILED ROLLBACK_COMPLETE DELETE_FAILED UPDATE_COMPLETE \
  --query "StackSummaries[?starts_with(StackName, 'workspace-')].StackName" --output text)"
for s in ${stacks}; do log "Deleting ${s}"; aws cloudformation delete-stack --stack-name "${s}"; done
for s in ${stacks}; do aws cloudformation wait stack-delete-complete --stack-name "${s}"; done

# Scheduled cleanup tasks are standalone (not a service), so Terraform won't
# stop them and the cluster can't be deleted while they run.
cluster="$(terraform -chdir="${TF_DIR}" output -raw ecs_cluster_name)"
tasks="$(aws ecs list-tasks --cluster "${cluster}" --family coder-cleanup-prod --query 'taskArns' --output text)"
for t in ${tasks}; do log "Stopping ${t##*/}"; aws ecs stop-task --cluster "${cluster}" --task "${t}" >/dev/null; done

zone="$(terraform -chdir="${TF_DIR}" output -raw domain_name)"
approve=()
[[ "${AUTO_APPROVE}" == "1" ]] && approve=(-auto-approve)
terraform -chdir="${TF_DIR}" destroy -input=false "${approve[@]}" \
  -var "aws_region=${AWS_REGION}" \
  -var "hosted_zone_name=${zone#coder.}"
