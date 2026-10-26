# Environment variable reference

All services read standard .NET configuration; every key can be set as an
environment variable using `__` (double underscore) separators. Docker
Compose passes them from `.env` (copy from `.env.example`).

## Database

| Variable | Used by | Meaning |
|----------|---------|---------|
| `ConnectionStrings__Default` | api, worker | Npgsql connection string (API account must NOT be the database superuser in production) |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | postgres, flyway | Container bootstrap credentials |
| `POSTGRES_HOST_PORT` | compose | Host port for Postgres (default 5433) |

## Authentication

| Variable | Used by | Meaning |
|----------|---------|---------|
| `Jwt__Key` | api, worker | HS256 signing key. **Required, ≥ 32 random chars.** The API refuses to start without it. Generate: `openssl rand -hex 32` |
| `Jwt__Issuer` | api, worker | Token issuer (default `af-foundation`) |
| `Jwt__Audience` | api, worker | Token audience (default `af-foundation-clients`) |
| `Jwt__AccessTokenMinutes` | api | Access-token lifetime (default 15) |
| `Jwt__RefreshTokenDays` | api | Refresh-session lifetime (default 30) |
| `Jwt__BootstrapKey` | api | While set AND no administrator exists, `POST /api/v1/admin/bootstrap` with this key promotes the caller. **Remove after first admin.** |

## CORS / hosting

| Variable | Used by | Meaning |
|----------|---------|---------|
| `Cors__AllowedOrigins` | api | JSON array or semicolon string of browser origins allowed to call the API |
| `ASPNETCORE_URLS` | api | Listen addresses (compose: `http://+:8080`) |
| `API_HOST_PORT`, `WEB_HOST_PORT` | compose | Host port mappings |

## Matching / requests / donations

| Variable | Used by | Meaning |
|----------|---------|---------|
| `Matching__RadiiKm` | api, worker | Radius ladder in km, comma-separated (default `25,50,100`; first entry = initial radius) |
| `Matching__WaveDelayMinutes` | worker | Delay between waves (default 20) |
| `Matching__BatchSizePerWave` | worker | Max donors notified per wave (default 40) |
| `Matching__MinCandidatesToStop` | worker | Enough candidates → stop the ladder (default 10) |
| `Requests__DefaultExpiryHours` | api | Default request expiry (default 48) |
| `Requests__MinExpiryHours` / `Requests__MaxExpiryHours` | api | Expiry bounds (1 / 168) |
| `Requests__MaxOpenRequestsPerUser` | api | Anti-spam cap (default 5) |
| `Donations__MaxReportAgeDays` | api | How far back a donation date may lie (default 30) |

## Notifications

| Variable | Used by | Meaning |
|----------|---------|---------|
| `OneSignal__AppId` | api, worker | OneSignal app id. **Leave empty for no-op push** (in-app history still works) |
| `OneSignal__ApiKey` | api, worker | OneSignal REST API key (REST API, base `https://api.onesignal.com`) |
| `Notifications__OutboxBatchSize` | worker | Delivery batch (default 20) |
| `Notifications__MaxAttempts` | worker | Retries before `failed` (default 8) |
| `Notifications__RetryBaseSeconds` / `RetryMaxSeconds` | worker | Backoff bounds (5 / 900) |
| `Worker__PollSeconds` | worker | Base poll interval (default 10) |
| `Worker__InstanceName` | worker | Lease owner tag shown in logs |

## Mobile app (build-time, `--dart-define`)

| Variable | Meaning |
|----------|---------|
| `API_BASE_URL` | API base, e.g. `https://api.example.org/api/v1` |
| `ONESIGNAL_APP_ID` | OneSignal app id; empty = push disabled |

## Website (build-time)

| Variable | Meaning |
|----------|---------|
| `VITE_API_BASE_URL` | Baked into the static bundle for deep links |

## Secrets handling

- Never commit real `.env` files (`.gitignore` blocks `.env*` except
  `.env.example`).
- Production secrets come from the deployment platform (Coolify environment
  variables or a secrets manager), not from images.
- The JWT key, DB password, OneSignal API key and bootstrap key are the four
  credentials that matter — see [security checklist](security-privacy-checklist.md).
