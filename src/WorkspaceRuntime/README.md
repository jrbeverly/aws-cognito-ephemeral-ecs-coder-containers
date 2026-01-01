# Workspace Runtime

`codercom/code-server` on port 7080 with `--auth none` and `HOME=/workspace` (EFS). The ALB's Cognito action is the only auth.

## Notes

- `coder server` (the Coder control plane) always forces the `/setup` first-admin page and has no auth-disabled mode; `CODER_EMAIL`/`CODER_PASSWORD` are not server settings, so it cannot be pre-seeded that way.
- Health check path is `/healthz`.
