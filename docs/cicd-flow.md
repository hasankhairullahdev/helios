# 🔄 Alur CI/CD Helios — End to End

## Big Picture

```
Developer                GitHub                  Cluster (kind/k3d)
    │                       │                           │
    │ git push / PR          │                           │
    ├──────────────────────► │                           │
    │                        │ trigger CI                │
    │                        ├───────────────────────────┤
    │                        │   GitHub Actions           │
    │                        │   (robot yang kerja)       │
    │                        │                            │
    │                        │ commit values-dev.yaml    │
    │                        ├───────────────────────────┤
    │                        │                   ArgoCD   │
    │                        │               (watch repo) │
    │                        │                     │      │
    │                        │                     │ sync │
    │                        │                     ├─────►│
    │                        │                            │ deploy
```

---

## FASE 1 — Developer Push Code

```
Developer nulis kode → git push ke branch feature
                     → buka Pull Request ke main
```

**Tools:** `Git`, `GitHub`  
**Fungsi:** Version control, code review sebelum masuk main

---

## FASE 2 — CI Triggered (`.github/workflows/ci.yaml`)

Begitu ada push/PR ke `main`, GitHub Actions otomatis jalan.

### Job 1: Build & Test (Matrix — Paralel!)

```
┌─────────────────────────┐    ┌─────────────────────────┐
│   task-service           │    │  notification-service    │
│                          │    │                          │
│  dotnet restore          │    │  dotnet restore          │
│  (cache NuGet packages)  │    │  (cache NuGet packages)  │
│       ↓                  │    │       ↓                  │
│  dotnet build            │    │  dotnet build            │
│       ↓                  │    │       ↓                  │
│  dotnet test             │    │  dotnet test             │
│  (publish .trx result)   │    │  (publish .trx result)   │
└─────────────────────────┘    └─────────────────────────┘
         kedua job jalan PARALEL — hemat waktu!
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `GitHub Actions` | Orchestrator, runner VM |
| `dotnet CLI` | Build & test .NET project |
| `actions/cache` | Cache NuGet supaya tidak download ulang tiap run |
| `dorny/test-reporter` | Publish hasil test sebagai annotation di PR |

---

### Job 2: Security Scan — Trivy (setelah Job 1 pass)

```
┌─────────────────────────┐    ┌─────────────────────────┐
│   task-service           │    │  notification-service    │
│                          │    │                          │
│  Trivy scan filesystem   │    │  Trivy scan filesystem   │
│  (source + NuGet deps)   │    │  (source + NuGet deps)   │
│       ↓                  │    │       ↓                  │
│  Ada HIGH/CRITICAL? ─────┼────┼──► FAIL pipeline! 🚨    │
│  Tidak ada? → PASS ✅    │    │  Tidak ada? → PASS ✅    │
└─────────────────────────┘    └─────────────────────────┘
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `Trivy (aquasecurity)` | Scanner CVE untuk dependency dan filesystem |

> **Kenapa ini penting:** Tangkap vulnerability SEBELUM image di-build dan di-push ke registry. Konsep "shift-left security" — semakin awal masalah ditemukan, semakin murah biaya fixnya.

---

### Job 3: Build & Push Image ke GHCR (hanya di `main` branch)

```
Checkout kode
     ↓
git rev-parse --short HEAD → dapat short SHA (misal: "abc1234")
     ↓
docker/buildx build --build-arg GIT_SHA=abc1234
     ↓
Push ke GHCR dengan DUA tag:
  ghcr.io/hasankhairullahdev/task-service:abc1234  ← SHA tag (immutable!)
  ghcr.io/hasankhairullahdev/task-service:main     ← floating tag (convenience)
     ↓
(sama untuk notification-service, paralel)
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `docker/login-action` | Login ke GHCR pakai GITHUB_TOKEN |
| `docker/setup-buildx-action` | Setup BuildKit untuk layer caching |
| `docker/build-push-action` | Build image dari Dockerfile + push |
| `GHCR` | Tempat nyimpen container images |

> **Kenapa SHA tag:** Immutable! Kita selalu tau persis commit mana yang running di cluster. `latest` tag tidak pernah dipakai untuk deployment.

---

### Job 4: Bump Dev Values (GitOps Write Step)

```
Ambil short SHA dari job sebelumnya (misal: "abc1234")
     ↓
sed replace:
  infra/helm/task-service/values-dev.yaml         → tag: "abc1234"
  infra/helm/notification-service/values-dev.yaml → tag: "abc1234"
     ↓
git commit -m "chore: bump dev image tags to abc1234 [skip ci]"
     ↓
git push ke main
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `sed` | Simple text replace di YAML file |
| `git` | Commit dan push perubahan |

> **`[skip ci]`** di commit message — mencegah CI trigger lagi secara rekursif!  
> **Kenapa ini GitOps:** CI tidak pernah `kubectl apply`! CI hanya ubah file Git. ArgoCD yang apply ke cluster.

---

## FASE 3 — ArgoCD Detect Perubahan

```
ArgoCD polling repo setiap ~3 menit
     ↓
Detect: infra/helm/task-service/values-dev.yaml berubah!
     ↓
Bandingkan dengan state di cluster helios-dev namespace
     ↓
OutOfSync detected → trigger sync (karena automated: true di dev)
     ↓
Helm template render dengan values baru
     ↓
kubectl apply semua manifest ke namespace helios-dev
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `ArgoCD` | GitOps controller, watch repo dan sync ke cluster |
| `Helm` | Render template YAML dari chart + values |
| `kubectl` | Apply manifest ke Kubernetes (dijalankan ArgoCD, bukan CI!) |

**Perbedaan Dev vs Staging/Prod:**

| Environment | Sync Policy | Artinya |
|---|---|---|
| `helios-dev` | `automated` | ArgoCD langsung deploy begitu ada perubahan |
| `helios-staging` | `manual` | ArgoCD show "OutOfSync", manusia yang klik Sync |
| `helios-prod` | `manual` | Sama seperti staging, tapi promotion butuh approval |

---

## FASE 4 — Kubernetes Deploy dengan Argo Rollouts

### DEV — Plain Deployment (cepat, tidak perlu canary)

```
ArgoCD apply Deployment
     ↓
Kubernetes rolling update biasa
  Pod v1 → Pod v2 (langsung ganti semua)
```

### STAGING / PROD — Rollout + Canary (task-service)

```
ArgoCD apply Rollout
     ↓
Argo Rollouts controller ambil alih
     ↓
Step 1: setWeight 10%
  10% traffic → Pod v2 (canary)
  90% traffic → Pod v1 (stable)
  pause 2 menit...
     ↓
Step 2: AnalysisRun query Prometheus:
  "Error rate canary pods < 1%?"    → PASS ✅
  "p95 latency canary < 500ms?"     → PASS ✅
     ↓
Step 3: setWeight 50%
  50% traffic → Pod v2
  pause 2 menit...
     ↓
Step 4: AnalysisRun lagi           → PASS ✅
     ↓
Step 5: setWeight 100%
  100% traffic → Pod v2
  Pod v1 dihapus ✅ DONE!
```

```
--- KALAU ANALYSIS GAGAL di step mana pun ---

  Error rate > 1% ATAU latency > 500ms
       ↓
  failureLimit tercapai (2x gagal berturut)
       ↓
  Auto abort + rollback: 100% traffic → Pod v1 🚨
  Pod v2 dihapus
  Alert dikirim
```

### STAGING / PROD — Blue-Green (notification-service)

```
Deploy versi baru (green) secara penuh
  100% traffic → Pod v1 (blue/active) — tidak berubah dulu
  Pod v2 (green/preview) siap, bisa di-test via preview service
     ↓
Engineer verify green healthy
     ↓
Manual promote → cutover instan
  100% traffic → Pod v2 (green) ✅
     ↓
Pod v1 (blue) dipertahankan 30 detik untuk rollback darurat
     ↓
Pod v1 dihapus
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `Argo Rollouts` | Controller untuk canary dan blue-green strategy |
| `Prometheus` | Sumber data untuk AnalysisTemplate query |
| `AnalysisTemplate` | Definisi pass/fail criteria saat canary berjalan |

---

## FASE 5 — Observability (Monitoring Terus-Menerus)

```
task-service / notification-service
     │
     │ push OTLP (traces + metrics + logs)
     ▼
OTel Collector
     │
     ├── metrics ──► Prometheus ──► Grafana
     │                                 │
     │                                 ├── Dashboard: Golden Signals
     │                                 │   (request rate, error rate,
     │                                 │    latency p50/p95/p99, saturation)
     │                                 │
     │                                 └── Dashboard: Canary vs Stable
     │                                     (split metrics per pod hash,
     │                                      live traffic weight gauge)
     │
     └── traces ──► (Tempo — future phase)
```

**Tools:**

| Tool | Fungsi |
|---|---|
| `OpenTelemetry SDK` | Auto-instrument .NET app (HTTP requests, DB queries) |
| `OTel Collector` | Router: terima dari app, kirim ke multiple backend |
| `Prometheus` | Time-series database untuk metrics |
| `Grafana` | Visualisasi dashboard dan alerting |
| `kube-prometheus-stack` | Helm chart yang install semua sekaligus |

---

## FASE 6 — Promotion ke Staging / Prod (Manual)

```
Engineer lihat: dev berjalan baik ✅
     ↓
GitHub → Actions → "Promote to Staging" (workflow_dispatch)
     ↓
Otomatis ambil SHA dari values-dev.yaml
     ↓
sed replace values-staging.yaml → git commit → git push
     ↓
ArgoCD detect OutOfSync di helios-staging
     ↓
Engineer klik "Sync" di ArgoCD UI
     ↓
Canary rollout dimulai di staging...
     ↓
(proses sama untuk prod, tapi butuh reviewer approval dulu)
```

---

## 🗺️ Master Summary — Semua Tools & Fungsinya

| Tools | Layer | Fungsi |
|---|---|---|
| **Git + GitHub** | Source Control | Version control, collaboration, PR review |
| **GitHub Actions** | CI | Otomatisasi: build, test, scan, push, bump values |
| **dotnet CLI** | Build | Compile + test .NET services |
| **Trivy** | Security | Scan CVE di dependency sebelum image di-push |
| **GHCR** | Registry | Simpan container images dengan immutable SHA tag |
| **ArgoCD** | GitOps | Watch repo, sync cluster state = Git state |
| **Helm** | Packaging | Template YAML manifest per environment |
| **Argo Rollouts** | Delivery | Canary + Blue-Green deployment strategy |
| **AnalysisTemplate** | Quality Gate | Auto pass/fail berdasarkan Prometheus query |
| **Prometheus** | Metrics | Kumpul dan simpan time-series metrics |
| **OTel Collector** | Telemetry | Router: terima OTLP dari app, kirim ke backend |
| **Grafana** | Visualization | Dashboard golden signals + canary vs stable |
| **kind** | Cluster | Local Kubernetes untuk dev/demo |
| **Kubernetes** | Orchestration | Run dan manage containers di cluster |

---

## 🔑 Prinsip Utama yang Harus Diingat

| # | Prinsip | Implementasinya di Helios |
|---|---|---|
| 1 | **Everything declarative** | Tidak ada `kubectl apply` manual — semua lewat Git + ArgoCD |
| 2 | **Immutable images** | Tag selalu SHA (`abc1234`), tidak pernah `latest` |
| 3 | **Environment parity** | Satu Helm chart, tiga `values-{env}.yaml` |
| 4 | **Progressive delivery** | Setiap deploy staging/prod lewat canary + analysis gate |
| 5 | **Rollback must be fast** | Argo Rollouts auto-rollback, atau `git revert` satu commit |
| 6 | **Shift-left security** | Trivy scan di CI sebelum image di-push |
