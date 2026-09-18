# Helios

Helios is a production-grade GitOps delivery platform built on top of two deliberately minimal .NET microservices. The application layer is intentionally simple — the point of this project is the **pipeline**: commit → CI → image → GitOps sync → progressive canary rollout → automated rollback.

## Services
| Service | Description |
|---|---|
| `task-service` | CRUD REST API for task items, backed by Postgres |
| `notification-service` | Subscribes to `TaskCreated` events and simulates notifications |

## Tech Stack
- **Runtime**: .NET 10, ASP.NET Core Minimal APIs
- **CI**: GitHub Actions — build, test, Trivy scan, push to GHCR, auto-bump Helm values
- **GitOps**: ArgoCD (App-of-Apps pattern) — cluster state is always a reflection of Git
- **Progressive Delivery**: Argo Rollouts (canary) with AnalysisTemplate gated on Prometheus metrics
- **Observability**: OpenTelemetry Collector → Prometheus + Grafana

## Phases
| Phase | What's built |
|---|---|
| 0 | Repo scaffolding, minimal service skeletons |
| 1 | App logic: EF Core CRUD, event broker, Serilog, OpenTelemetry |
| 2 | Multi-stage Dockerfiles with baked-in Git SHA |
| 3 | CI pipeline: matrix build, Trivy scan, GHCR push, dev auto-deploy |
| 4 | Helm charts for all environments (dev/staging/prod) |
| 5 | ArgoCD App-of-Apps, automated sync for dev |
| 6 | Argo Rollouts canary + Prometheus AnalysisTemplate |
| 7 | OTel Collector, Prometheus, Grafana dashboards |
| 8 | Chaos demo — deliberate regression caught by canary analysis, auto-rollback |

## Core Principles
1. **Everything declarative** — no manual `kubectl apply` once ArgoCD is live
2. **Immutable images** — always tagged with the Git SHA, never `latest`
3. **Environment parity** — one Helm chart, three `values-{env}.yaml` files
4. **Progressive delivery is not optional** — every prod deploy goes through canary + analysis gate
5. **Rollback is provably fast** — each phase includes a break-and-rollback demo
