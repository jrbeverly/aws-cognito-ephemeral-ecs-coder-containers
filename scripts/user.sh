#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TF_DIR="${ROOT_DIR}/env/shared-platform/prod"
AWS_REGION="${AWS_REGION:-ca-central-1}"
USER_EMAIL="${USER_EMAIL:?Set USER_EMAIL=you@example.com}"
USER_PASSWORD="${USER_PASSWORD:-Ws1!$(openssl rand -hex 8)}"
export AWS_REGION AWS_DEFAULT_REGION="${AWS_REGION}"

pool="$(terraform -chdir="${TF_DIR}" output -raw cognito_user_pool_id)"

if ! aws cognito-idp admin-get-user --user-pool-id "${pool}" --username "${USER_EMAIL}" >/dev/null 2>&1; then
  aws cognito-idp admin-create-user --user-pool-id "${pool}" --username "${USER_EMAIL}" \
    --message-action SUPPRESS \
    --user-attributes Name=email,Value="${USER_EMAIL}" Name=email_verified,Value=true >/dev/null
fi
aws cognito-idp admin-set-user-password --user-pool-id "${pool}" --username "${USER_EMAIL}" \
  --password "${USER_PASSWORD}" --permanent

printf '[user] %s / %s\n' "${USER_EMAIL}" "${USER_PASSWORD}"
