# Rules: Kubernetes & Helm

Scope: `infra/helm/`

## Chart structure (per service)
```
infra/helm/task-service/
  Chart.yaml
  values.yaml            # defaults, never deploy from this directly
  values-dev.yaml
  values-staging.yaml
  values-prod.yaml
  templates/
    deployment.yaml       # actually a Rollout, see argocd-rollouts.md
    service.yaml
    configmap.yaml
    hpa.yaml
    servicemonitor.yaml   # Prometheus scrape config
    _helpers.tpl
```

## Non-negotiables
- **Resource requests AND limits** set for every container — no chart deploys
  without them. Values differ per environment (prod gets real numbers, dev
  can be generous/loose).
- **Liveness/readiness probes** wired to `/healthz` and `/readyz` from the
  app rules — different initialDelay/period per environment is fine, the
  paths are not.
- **HorizontalPodAutoscaler** included (even if disabled in dev via a values
  flag) targeting CPU + memory.
- **PodDisruptionBudget** for staging/prod so voluntary disruptions (node
  drains) don't take out all replicas at once.
- **No `latest` image tag ever referenced in a template default** — the
  `values.yaml` default should intentionally fail closed (e.g. `tag: ""`)
  so a chart can never silently deploy without an explicit tag.
- **NetworkPolicy** — default-deny ingress, explicit allow rules between
  task-service and notification-service only.

## Environment differences (via values files only, never template forks)
| | dev | staging | prod |
|---|---|---|---|
| replicas | 1 | 2 | 3+ (HPA-driven) |
| resources | loose | moderate | tight, tuned |
| PDB | off | on | on |
| autoscaling | off | on | on |

## Local dev loop
- Provide a `Makefile` or `just` recipe to spin up `kind`/`k3d`, install
  ArgoCD + Argo Rollouts CRDs, and point the App-of-Apps root at the local
  cluster — so the whole pipeline is demoable without a cloud bill.

## What NOT to do
- Don't template environment-specific business logic into the chart with
  `if` conditions sprawling everywhere — if staging/prod genuinely need
  structurally different resources (not just different values), that's a
  sign it should be a separate optional subchart, not a giant conditional
  block.
