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

The Worker is capped by default at 256 MiB RAM, 0.25 CPU, 128 processes, and
one logical .NET processor. Deploy builds are serialized with Compose
parallelism set to 1 so the first image build does not saturate the host.

Before the first production run, inspect the target directory and existing
containers. If SwapKino data already exists, create a database backup. Every
subsequent deployment creates a timestamped PostgreSQL backup under
`/projects/SwapKino/backups/` when the Compose PostgreSQL service is running.

## VPS snapshot

Audited on 2026-08-18: Ubuntu 24.04.4 LTS, Docker 29.6.1, 3.8 GiB RAM,
512 MiB swap, and about 1.9 GiB available memory at audit time. Existing
workloads include `gunesh-workers` and `pomoshnik-bot`; SwapKino uses its own
Compose project and localhost port `18080`.
