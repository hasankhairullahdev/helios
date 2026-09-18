# PHASE_PROMPTS.md

Run these in order. Don't start a phase until the previous one's acceptance
criteria are demonstrably met. Each prompt assumes Bob has already read
`AGENTS.md` and the relevant files in `.bob/rules/`.

---

## Phase 0 — Repo Scaffolding
**Prompt:** "Set up the repository structure exactly as described in
AGENTS.md. Create both service folders with a minimal `Program.cs` that just
returns 200 on `/healthz`, `/readyz`, and `/version`. No business logic yet.
Add a root `.gitignore`, `README.md` titled 'Helios' with a one-paragraph
project summary, and empty placeholder folders for `infra/`."
**Acceptance:** Both services run locally with `dotnet run` and respond on
all three endpoints.

## Phase 1 — Application Logic
**Prompt:** "Following `.bob/rules/architecture.md`, implement task-service's
CRUD endpoints with EF Core + Postgres, and notification-service's event
subscriber. Wire up Serilog and OpenTelemetry instrumentation as specified.
Add a docker-compose.yml for local Postgres + broker so both services run
end-to-end locally."
**Acceptance:** `POST /tasks` on task-service results in a log line in
notification-service confirming the event was received, all running via
docker-compose.

## Phase 2 — Dockerfiles
**Prompt:** "Write multi-stage Dockerfiles for both services per
`architecture.md`'s Dockerfile conventions, including the `GIT_SHA` build arg
wired to `/version`."
**Acceptance:** `docker build --build-arg GIT_SHA=$(git rev-parse --short HEAD)`
produces a working image under ~150MB for each service, and `/version`
reflects the SHA.

## Phase 3 — CI Pipeline
**Prompt:** "Implement `.github/workflows/ci.yaml` per `cicd.md`: matrix
build+test, Trivy scan, conditional image build/push to GHCR tagged with the
short SHA, and the dev values-file bump-and-commit step."
**Acceptance:** A PR merge to `main` results in a new image in GHCR and an
automatic commit updating `values-dev.yaml` with the new tag.

## Phase 4 — Helm Charts
**Prompt:** "Create Helm charts for both services per
`kubernetes-helm.md`, including the environment values files and all
non-negotiables (probes, resources, PDB, HPA, NetworkPolicy,
ServiceMonitor). Use a plain Deployment template for now — Rollout comes in
Phase 6."
**Acceptance:** `helm template` renders valid manifests for all three
environments; `helm install` onto a local kind/k3d cluster succeeds and both
services are reachable in-cluster.

## Phase 5 — ArgoCD Setup
**Prompt:** "Install ArgoCD on the local cluster and create the App-of-Apps
structure per `argocd-rollouts.md`: root Application plus one child
Application per service per environment, dev on automated sync."
**Acceptance:** A commit changing `values-dev.yaml` (e.g. from the Phase 3
CI job) is picked up by ArgoCD and auto-deployed within the sync interval,
visible in the ArgoCD UI.

## Phase 6 — Argo Rollouts & Canary
**Prompt:** "Replace the Deployment template with a Rollout resource per
`argocd-rollouts.md`'s canary strategy for staging/prod. Set up the
AnalysisTemplate querying Prometheus for error rate and p95 latency (stand
up Prometheus first if not already present)."
**Acceptance:** Promoting a new image to staging triggers a visible
step-by-step canary rollout in `kubectl argo rollouts get rollout
task-service --watch`.

## Phase 7 — Observability
**Prompt:** "Deploy the OTel Collector, Prometheus, and Grafana per
`observability.md`. Build the two dashboards described (golden signals,
canary-vs-stable comparison)."
**Acceptance:** During a Phase 6 canary rollout, the Grafana dashboard shows
canary and stable metrics side by side in real time.

## Phase 8 — Chaos Demo (the payoff)
**Prompt:** "Deliberately introduce a regression in task-service (e.g. an
endpoint that returns 500s 20% of the time) behind a feature branch. Promote
it to staging through the normal pipeline and let the canary AnalysisTemplate
catch it."
**Acceptance:** The rollout auto-aborts and shifts traffic back to stable
without manual intervention — this is the flagship demo/recording for the
portfolio.

## Phase 9 — Stretch (optional, pick what's interesting)
- Blue-green strategy for notification-service (compare/contrast with
  task-service's canary in a README write-up).
- External Secrets Operator + a KMS backend for real secret management.
- Terraform/Bicep to provision an actual AKS cluster, migrate the whole demo
  off local kind/k3d.
- A short architecture-decision-record (ADR) folder documenting *why* each
  major choice was made — genuinely useful interview material.
