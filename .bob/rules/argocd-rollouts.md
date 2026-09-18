# Rules: ArgoCD & Argo Rollouts

Scope: `infra/argocd/`, `infra/rollouts/`

## App-of-Apps pattern
- One root `Application` (`app-of-apps.yaml`) that points at
  `infra/argocd/apps/` — ArgoCD syncs that directory, which itself contains
  one `Application` manifest per service per environment.
- Each child `Application` points at `infra/helm/<service>` with
  `valueFiles: [values-<env>.yaml]` and its own destination namespace
  (`task-service-dev`, `task-service-staging`, `task-service-prod`, etc).
- `syncPolicy.automated` with `selfHeal: true` and `prune: true` for
  **dev only**. Staging/prod stay on manual sync initially — flip to
  automated once the canary + analysis gate story (below) is proven, so the
  first manual→automated transition is itself a demoable milestone.

## Argo Rollouts (replaces the plain Deployment)
- `templates/deployment.yaml` in each Helm chart is actually a `Rollout`
  resource (`argoproj.io/v1alpha1`), not a vanilla `apps/v1 Deployment`.
- **Canary strategy** for staging/prod:
  ```yaml
  strategy:
    canary:
      steps:
        - setWeight: 10
        - pause: {duration: 2m}
        - analysis:
            templates:
              - templateName: success-rate-and-latency
        - setWeight: 50
        - pause: {duration: 2m}
        - analysis:
            templates:
              - templateName: success-rate-and-latency
        - setWeight: 100
  ```
- **dev** can use a plain Rollout with no canary steps (weight 100
  immediately) — canary discipline matters for staging/prod, not dev.

## AnalysisTemplate (the actual "advanced" part)
- Query Prometheus for:
  - HTTP error rate (5xx / total) over the canary window — fail if above
    threshold (e.g. > 1%).
  - p95 latency — fail if it regresses beyond a defined percentage vs the
    stable version's baseline.
- `AnalysisTemplate` should reference a `PrometheusAddon` provider pointing
  at the in-cluster Prometheus, with `failureLimit` low (1-2) so a bad
  canary aborts fast.
- On analysis failure, Argo Rollouts auto-aborts and shifts traffic back to
  the stable ReplicaSet — this is the "automated rollback" story, demo it by
  deliberately shipping a broken version (e.g. an endpoint that throws 500s)
  and showing the rollout abort itself without human intervention.

## Blue-green as an alternative (stretch)
- Implement `notification-service` with blue-green instead of canary
  (`strategy.blueGreen`) so the portfolio shows both strategies and you can
  articulate the trade-off (instant full cutover + easy rollback vs gradual
  exposure) in an interview/demo.

## Manual rollback path
- Even with automated analysis, document the one-command manual path:
  `kubectl argo rollouts undo <rollout-name>` or a plain `git revert` on the
  values file commit — both should work, and both should be demoed once.
