# Phase 8 — Chaos Demo Runbook

Tujuan: membuktikan bahwa AnalysisTemplate Argo Rollouts mendeteksi error rate
tinggi pada canary pods dan **otomatis abort rollout** tanpa intervensi manual.
Ini adalah flagship demo untuk portfolio.

---

## Pra-syarat

- kind cluster `helios` running: `kind get clusters` → harus ada `helios`
- Podman machine running: `podman machine start`
- Port-forward ArgoCD aktif (lihat di bawah)
- Port-forward Grafana aktif (lihat di bawah)
- `chaos/bad-post` branch sudah di-push dan CI sudah build image baru ke GHCR

---

## Langkah 0 — Start Cluster & Port-Forwards

```powershell
# Start Podman machine kalau belum
podman machine start

# Verify cluster running
kubectl cluster-info --context kind-helios

# Port-forward ArgoCD UI (buka tab baru di terminal)
kubectl port-forward svc/argocd-server -n argocd 9090:443

# Port-forward Grafana (buka tab baru di terminal)
kubectl port-forward svc/prometheus-grafana -n monitoring 3000:80
```

---

## Langkah 1 — Pastikan AnalysisTemplate Sudah di-Apply

AnalysisTemplate harus ada di namespace `task-service-staging` sebelum rollout
dimulai. Cek dulu:

```powershell
kubectl get analysistemplate -n task-service-staging
```

Kalau belum ada, apply:

```powershell
kubectl apply -f infra/rollouts/analysis-template.yaml -n task-service-staging
```

Verify:

```powershell
kubectl describe analysistemplate success-rate-and-latency -n task-service-staging
```

---

## Langkah 2 — Pastikan Stable Version Running di Staging

Sebelum inject chaos, kita butuh stable version yang sudah running.
Cek status rollout saat ini:

```powershell
kubectl argo rollouts get rollout task-service -n task-service-staging
```

Output yang diharapkan: status `Healthy`, 2 replicas running.

---

## Langkah 3 — Promote Chaos Build ke Staging

Buka GitHub → Actions → **Promote to Staging** → klik **Run workflow**.

Isi field:
- **Branch:** `main`
- **Service:** `task-service`
- **Image tag:** `<SHA dari build chaos/bad-post>` *(lihat di GHCR atau CI log)*

Ini akan update `infra/helm/task-service/values-staging.yaml` dengan SHA baru
dan push commit ke repo. ArgoCD akan pick up perubahan ini dan trigger rollout.

---

## Langkah 4 — Deploy Traffic Generator

Saat canary mulai (setelah promote), langsung deploy traffic generator.
Job ini akan terus-terusan hit `/tasks` sehingga Prometheus punya data.

```powershell
kubectl apply -f infra/chaos/traffic-generator.yaml

# Monitor log traffic generator
kubectl logs -f job/traffic-generator -n task-service-staging
```

Output yang diharapkan: setiap request ke-3 akan terlihat `FAILED with HTTP 500`.

---

## Langkah 5 — Watch Canary Rollout

Buka terminal baru dan watch rollout secara real-time:

```powershell
kubectl argo rollouts get rollout task-service -n task-service-staging --watch
```

Alur yang akan terlihat:
1. `Progressing` — canary pods starting (10% weight)
2. `Paused` — menunggu 2 menit (sesuai `pause: {duration: 2m}`)
3. `Running` analysis — AnalysisTemplate mulai query Prometheus
4. `Degraded` / `Aborted` — error rate terdeteksi > 1%, rollout di-abort
5. Traffic kembali 100% ke stable pods

---

## Langkah 6 — Monitor di ArgoCD UI

Buka: **https://localhost:9090**
- Login: `admin` / `1WkWEchJS6OQvTgP`
- Buka app `task-service-staging`
- Lihat Rollout resource status berubah dari `Progressing` → `Degraded`

---

## Langkah 7 — Monitor di Grafana (Screenshot untuk Portfolio!)

Buka: **http://localhost:3000**
- Login: `admin` / `helios-grafana`
- Buka dashboard **Canary vs Stable**
- Pastikan panel menunjukkan:
  - Canary pods: error rate spike tinggi (~33%)
  - Stable pods: error rate normal (~0%)
  - Rollout weight kembali ke 0% canary

**Screenshot dashboard ini** — ini adalah bukti portfolio utama.

---

## Langkah 8 — Verifikasi Rollback Berhasil

```powershell
# Status rollout harus Degraded/Aborted
kubectl argo rollouts get rollout task-service -n task-service-staging

# Pastikan tidak ada canary pods yang masih running
kubectl get pods -n task-service-staging -l app=task-service

# Semua pods harus pakai image stable (SHA lama), bukan chaos build
kubectl get pods -n task-service-staging -l app=task-service \
  -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.spec.containers[0].image}{"\n"}{end}'
```

---

## Langkah 9 — Manual Rollback Path (Demo Alternatif)

Tunjukkan juga bahwa rollback bisa dilakukan manual dalam satu command:

```powershell
# Option A: Argo Rollouts CLI
kubectl argo rollouts undo task-service -n task-service-staging

# Option B: Git revert (GitOps way)
# Revert commit yang bump values-staging.yaml
git revert HEAD --no-edit
git push
# ArgoCD akan sync dan rollout kembali ke SHA sebelumnya
```

---

## Langkah 10 — Cleanup

Setelah demo selesai, hapus traffic generator dan reset ke stable build:

```powershell
# Hapus traffic generator job
kubectl delete job traffic-generator -n task-service-staging

# Promote stable build ke staging (hapus chaos image tag)
# Edit infra/helm/task-service/values-staging.yaml
# Set tag: kembali ke SHA stable terakhir yang diketahui
```

---

## Expected Outcome (Acceptance Criteria Phase 8)

✅ Rollout auto-abort tanpa human intervention  
✅ Traffic 100% kembali ke stable ReplicaSet  
✅ Grafana dashboard menunjukkan error spike pada canary pods  
✅ AnalysisTemplate log menunjukkan `error-rate` metric melebihi threshold  
✅ Screenshot sebagai portfolio evidence  

---

## Troubleshooting

### AnalysisTemplate query returns `no data`

Prometheus butuh beberapa menit untuk scrape metrics pertama kali.
Pastikan traffic generator sudah kirim minimal 10-20 requests sebelum analysis
dimulai. Kalau masih no data, cek ServiceMonitor:

```powershell
kubectl get servicemonitor -n task-service-staging
kubectl get servicemonitor -n monitoring
```

### Rollout tidak trigger setelah values bump

Cek ArgoCD sync status:

```powershell
kubectl get application task-service-staging -n argocd -o jsonpath='{.status.sync.status}'
```

Kalau `OutOfSync`, force sync:

```powershell
kubectl patch application task-service-staging -n argocd \
  --type merge -p '{"operation":{"initiatedBy":{"username":"admin"},"sync":{}}}'
```

### Canary pods crash sebelum analysis mulai

Cek apakah image berhasil di-pull:

```powershell
kubectl describe pod -n task-service-staging -l rollouts-pod-template-hash
```

Pastikan GHCR image public atau imagePullSecret sudah di-configure di namespace.
