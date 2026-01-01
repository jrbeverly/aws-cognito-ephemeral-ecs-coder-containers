#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TF_DIR="${ROOT_DIR}/env/shared-platform/prod"
AWS_REGION="${AWS_REGION:-ca-central-1}"
HOSTED_ZONE="${HOSTED_ZONE:-}"
AUTO_APPROVE="${AUTO_APPROVE:-0}"
USER_EMAIL="${USER_EMAIL:-}"
export AWS_REGION AWS_DEFAULT_REGION="${AWS_REGION}"

log() { printf '[deploy] %s\n' "$*"; }
fail() { printf '[deploy] error: %s\n' "$*" >&2; exit 1; }

if [[ -z "${HOSTED_ZONE}" ]]; then
  zones="$(aws route53 list-hosted-zones \
    --query 'HostedZones[?Config.PrivateZone==`false`].Name' \
    --output text | tr '\t' '\n' | sed 's/\.$//;/^$/d')"
  [[ -n "${zones}" && "$(wc -l <<<"${zones}")" == "1" ]] \
    || fail "Set HOSTED_ZONE; expected exactly one public hosted zone, found: ${zones:-none}"
  HOSTED_ZONE="${zones}"
fi
log "Hosted zone ${HOSTED_ZONE} (platform at coder.${HOSTED_ZONE})"

approve=()
[[ "${AUTO_APPROVE}" == "1" ]] && approve=(-auto-approve)

terraform -chdir="${TF_DIR}" init -input=false >/dev/null
terraform -chdir="${TF_DIR}" apply -input=false "${approve[@]}" \
  -var "aws_region=${AWS_REGION}" \
  -var "hosted_zone_name=${HOSTED_ZONE}"

out="$(terraform -chdir="${TF_DIR}" output -json)"
o() { jq -r ".$1.value" <<<"${out}"; }

registry="$(o control_plane_repository_url)"; registry="${registry%%/*}"
aws ecr get-login-password | docker login --username AWS --password-stdin "${registry}"

log "Building the workspace runtime image"
docker build --platform linux/amd64 -t "$(o workspace_runtime_repository_url):latest" "${ROOT_DIR}/src/WorkspaceRuntime"
docker push "$(o workspace_runtime_repository_url):latest"

log "Building the control-plane image (portal + Provisioning API + cleanup worker)"
docker build --platform linux/amd64 \
  -f "${ROOT_DIR}/Dockerfile.control-plane" \
  --build-arg "VITE_COGNITO_DOMAIN=$(o cognito_user_pool_domain)" \
  --build-arg "VITE_COGNITO_CLIENT_ID=$(o cognito_portal_client_id)" \
  -t "$(o control_plane_repository_url):latest" "${ROOT_DIR}"
docker push "$(o control_plane_repository_url):latest"

log "Rolling the control-plane service"
aws ecs update-service --cluster "$(o ecs_cluster_name)" --service "$(o control_plane_service_name)" \
  --force-new-deployment >/dev/null
aws ecs wait services-stable --cluster "$(o ecs_cluster_name)" --services "$(o control_plane_service_name)"

if [[ -n "${USER_EMAIL}" ]]; then
  USER_EMAIL="${USER_EMAIL}" "${ROOT_DIR}/scripts/user.sh"
fi

log "Portal: $(o portal_url)"
