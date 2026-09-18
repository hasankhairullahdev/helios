# Rules: Observability

Scope: `infra/observability/`

## Purpose
This isn't decorative — the metrics produced here are what the Argo Rollouts
AnalysisTemplate queries to decide canary pass/fail. If this stack is wrong,
the "automated rollback" story doesn't work.

## Stack
- **OpenTelemetry Collector** deployed in-cluster (DaemonSet or Deployment,
  Deployment is simpler for this scale), receiving OTLP from both services
  (traces, metrics, logs).
- Collector exports:
  - Metrics → Prometheus (via `prometheusexporter` or remote-write).
  - Traces → Tempo or Jaeger (pick one; Tempo pairs naturally with Grafana).
  - Logs → Loki (optional stretch; console+kubectl logs is acceptable if
    time-constrained).
- **Prometheus** — scrapes the collector's metrics endpoint, and/or each
  service directly via the `ServiceMonitor` defined in the Helm chart
  (`kubernetes-helm.md`).
- **Grafana** — at minimum two dashboards:
  1. Per-service golden signals (request rate, error rate, p50/p95/p99
     latency, saturation).
  2. A rollout-focused dashboard showing canary vs stable version metrics
     side by side during an active rollout — this is the one to screenshot
     for the portfolio.

## Metrics that matter for the AnalysisTemplate
- `http_server_requests_error_rate` (or equivalent OTel semantic convention
  metric) split by `rollouts-pod-template-hash` label so canary vs stable
  can be queried separately.
- `http_server_duration` histogram for p95 latency, same label split.

## Alerting (stretch)
- A couple of Prometheus alerting rules (error rate spike, pod
  crash-looping) wired to a dummy webhook receiver — enough to demonstrate
  the pattern, doesn't need to page anyone real.

## What NOT to do
- Don't build custom instrumentation when OTel auto-instrumentation for
  ASP.NET Core covers it — only add manual spans/metrics where they add
  real signal (e.g. a custom span around the notification-publish call).
