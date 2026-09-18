# AGENTS.md — Helios: Multi-Environment GitOps Platform for .NET Microservices

## Project Goal
Build a small, deliberately minimal set of .NET services whose *application* code
is simple — the point of this project is the **delivery pipeline**, not the
domain logic. Every phase should demonstrate a real, production-grade DevOps
practice end-to-end: commit → CI → image → GitOps sync → progressive rollout →
automated rollback.

Do not over-engineer the application layer. Do over-engineer the pipeline.

## Tech Stack
- **App runtime**: .NET 10, ASP.NET Core Minimal APIs (2 services — see below)
- **Containerization**: Docker, multi-stage builds, distroless/Alpine final stage
- **CI**: GitHub Actions
- **Registry**: GitHub Container Registry (GHCR)
- **Security scanning**: Trivy (image scan), optionally CodeQL (SAST)
- **Orchestration**: Kubernetes (local: kind or k3d; cloud target: AKS, stretch goal)
- **Packaging**: Helm charts, one per service
- **GitOps**: ArgoCD, App-of-Apps pattern
- **Progressive delivery**: Argo Rollouts (canary), AnalysisTemplate gated on Prometheus metrics
- **Observability**: OpenTelemetry Collector → Prometheus + Grafana
- **Secrets** (stretch): External Secrets Operator + a KMS/Vault backend
- **IaC** (stretch): Terraform or Bicep for cluster + networking provisioning

## Services (deliberately minimal domain)
1. **task-service** — CRUD for simple task items (Postgres). Exists to have
   something with state and a database migration story.
2. **notification-service** — receives an event when a task is created and
   logs/simulates sending a notification. Exists to prove inter-service
   communication and give the pipeline two independently deployable units.

No auth, no complex business rules, no event sourcing here — that complexity
lives in other portfolio projects. Keep these services boring on purpose.

## Repository Layout
```
repo/
  services/
    task-service/
    notification-service/
  infra/
    helm/
      task-service/
      notification-service/
    argocd/
      app-of-apps.yaml
      apps/
        task-service-dev.yaml
        task-service-staging.yaml
        task-service-prod.yaml
        notification-service-dev.yaml
        ...
    rollouts/
      task-service-rollout.yaml
      notification-service-rollout.yaml
    observability/
      otel-collector-config.yaml
      grafana-dashboards/
  .github/
    workflows/
      ci.yaml
      image-scan.yaml
  AGENTS.md
  .bob/
    rules/
      architecture.md
      cicd.md
      kubernetes-helm.md
      argocd-rollouts.md
      observability.md
  PHASE_PROMPTS.md
```

## Core Principles (apply to every phase)
1. **Everything declarative.** No `kubectl apply` by hand once ArgoCD is live —
   the only way to change the cluster is a Git commit.
2. **Immutable images.** Tag images with the Git SHA, never reuse `latest` for
   deployment. ArgoCD watches for tag changes in the Helm values, not for
   image rebuilds.
3. **Environment parity.** dev/staging/prod use the *same* Helm chart with
   different `values-{env}.yaml` — never diverge the templates themselves.
4. **Progressive delivery is not optional.** Every deploy to prod goes through
   a canary step with an automated analysis gate before full rollout — no
   direct-to-100% deploys once Argo Rollouts is in place.
5. **Rollback must be provably fast.** Each phase that touches deployment
   should end with a demo: break something on purpose, show the automated (or
   one-command Git revert) rollback.

## Working with Bob
Read `.bob/rules/*.md` before generating code or manifests — each rules file
covers conventions for its layer. Work through `PHASE_PROMPTS.md` in order;
do not skip ahead to ArgoCD/Rollouts config before the CI pipeline in that
phase is green.
