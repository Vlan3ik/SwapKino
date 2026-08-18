# Production deployment

Production is deployed from `main` by `.github/workflows/deploy-production.yml`.
The workflow uses the isolated checkout `/projects/SwapKino` and Compose project
`swapkino-prod`; it does not remove Docker volumes.

## GitHub Actions secrets

Configure these repository secrets before merging to `main`:

- `DEPLOY_HOST` — `78.17.55.13`;
- `DEPLOY_USER` — the dedicated non-root deployment user;
- `DEPLOY_SSH_KEY` — a dedicated Ed25519 private key whose public key is installed
  in that user's `~/.ssh/authorized_keys`.

Do not store the server password in GitHub, the repository, or workflow logs. The
password previously shared in chat must be rotated/revoked.

## Server prerequisites

The deployment user must have access to `/projects`, Docker Compose, `git`,
`curl`, `gzip`, `pg_dump`, and `ss`. Before the first deployment, provision
`/projects/SwapKino/backend/.env` from `backend/.env.example` with production
credentials. The workflow refuses to deploy if that file is absent.

The existing reverse proxy should keep serving `itdpy.xyz` and add only this
route:

```text
swapkino.itdpy.xyz -> http://127.0.0.1:18080
```

Keep the existing HTTPS/certificate configuration. Do not bind SwapKino to the
host's public ports 80 or 443. If `127.0.0.1:18080` belongs to another service,
the workflow stops instead of changing that service.

Before the first production run, inspect the target directory and existing
containers. If SwapKino data already exists, create a database backup. Every
subsequent deployment creates a timestamped PostgreSQL backup under
`/projects/SwapKino/backups/` when the Compose PostgreSQL service is running.
