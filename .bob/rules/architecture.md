# Rules: Application Architecture

Scope: `services/task-service`, `services/notification-service`

## Structure
- Minimal API style (no Controllers), one `Program.cs` per service plus a
  small `Endpoints/`, `Models/`, `Data/` folder split. Do not introduce Clean
  Architecture layering here — that's intentionally out of scope for this
  project so the pipeline stays the focus.
- `task-service`: Postgres via EF Core, one `Tasks` table, standard CRUD
  endpoints (`GET /tasks`, `POST /tasks`, `PATCH /tasks/{id}`, `DELETE`).
  On successful `POST`, publish a `TaskCreated` event.
- `notification-service`: subscribes to `TaskCreated` (simple message broker —
  RabbitMQ or even a lightweight in-cluster queue is fine), logs a simulated
  notification. No real email/SMS integration.

## Non-negotiables for every service
- **`/healthz` (liveness)** and **`/readyz` (readiness)** endpoints — Kubernetes
  probes depend on these existing and being meaningfully different (readiness
  should check DB connectivity, liveness should not).
- **Structured logging** via Serilog, JSON console sink (so it's scrapeable in
  cluster).
- **OpenTelemetry instrumentation from day one** — traces, metrics, and logs,
  even before the collector exists. Add `OTEL_EXPORTER_OTLP_ENDPOINT` as an
  env var read at startup, don't hardcode.
- **Config via environment variables**, following 12-factor — no
  `appsettings.Production.json` committed with real values. Helm will inject
  these.
- **`/version` endpoint** returning the Git SHA baked in at build time (via
  `--build-arg` in the Dockerfile) — this makes it trivially easy to verify
  in a demo which exact commit is running in which environment.

## Dockerfile conventions
- Multi-stage: `sdk` image for build/publish, minimal runtime image
  (`aspnet:10.0-alpine` or `chiseled`) for final stage.
- Non-root user in the final image.
- `ARG GIT_SHA` threaded through to the `/version` endpoint.
- Keep final image layers minimal — no shell utilities beyond what's needed.

## What NOT to build
- No auth/authz (defer entirely, consistent with other portfolio projects).
- No event sourcing / CQRS — plain CRUD is the point here.
- No frontend — this project is pipeline-only, curl/Postman is enough to
  demo the app layer.
