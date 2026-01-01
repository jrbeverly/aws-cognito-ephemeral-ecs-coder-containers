.PHONY: test deploy user destroy

AWS_REGION ?= ca-central-1
# Empty: use the account's only public Route 53 zone.
HOSTED_ZONE ?=
AUTO_APPROVE ?= 0
USER_EMAIL ?=

export AWS_REGION HOSTED_ZONE AUTO_APPROVE USER_EMAIL

test:
	dotnet test tests/ProvisioningService/ProvisioningService.Tests/
	dotnet test tests/CleanupService/CleanupService.Tests/
	cd app/PortalService && npm ci && npm run build

deploy:
	bash ./scripts/deploy.sh

user:
	bash ./scripts/user.sh

destroy:
	bash ./scripts/destroy.sh
