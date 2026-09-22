#!/usr/bin/env bash
# =============================================================================
# setup-staging-demo.sh
# Provision staging environment di kind cluster untuk Phase 8 chaos demo.
#
# Yang dilakukan script ini:
#  1. Install Postgres + RabbitMQ di helios-staging namespace
#  2. Buat secrets yang dibutuhkan task-service
#  3. Apply AnalysisTemplate ke namespace
#  4. Deploy stable version task-service via Helm (image harus sudah ada di GHCR)
#
# Usage:
#   bash infra/scripts/setup-staging-demo.sh <STABLE_SHA> <CHAOS_SHA>
#
# Example:
#   bash infra/scripts/setup-staging-demo.sh abc1234 def5678
# =============================================================================
set -euo pipefail

STABLE_SHA="${1:?Usage: $0 <STABLE_SHA> <CHAOS_SHA>}"
CHAOS_SHA="${2:?Usage: $0 <STABLE_SHA> <CHAOS_SHA>}"
NAMESPACE="helios-staging"
IMAGE_REPO="ghcr.io/hasankhairullahdev/task-service"

echo "==> Setting up staging environment for chaos demo"
echo "    Stable SHA : $STABLE_SHA"
echo "    Chaos SHA  : $CHAOS_SHA"
echo "    Namespace  : $NAMESPACE"
echo ""

# ─── 1. Namespace ────────────────────────────────────────────────────────────
echo "==> [1/6] Creating namespace $NAMESPACE..."
kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -

# ─── 2. Postgres ─────────────────────────────────────────────────────────────
echo "==> [2/6] Deploying Postgres..."
kubectl apply -n "$NAMESPACE" -f - <<'EOF'
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
        - name: postgres
          image: postgres:16-alpine
          env:
            - name: POSTGRES_DB
              value: tasks
            - name: POSTGRES_USER
              value: app
            - name: POSTGRES_PASSWORD
              value: helios-demo-secret
          ports:
            - containerPort: 5432
          resources:
            requests:
              cpu: "100m"
              memory: "128Mi"
            limits:
              cpu: "500m"
              memory: "256Mi"
---
apiVersion: v1
kind: Service
metadata:
  name: postgres
spec:
  selector:
    app: postgres
  ports:
    - port: 5432
      targetPort: 5432
EOF

# ─── 3. RabbitMQ ─────────────────────────────────────────────────────────────
echo "==> [3/6] Deploying RabbitMQ..."
kubectl apply -n "$NAMESPACE" -f - <<'EOF'
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rabbitmq
spec:
  replicas: 1
  selector:
    matchLabels:
      app: rabbitmq
  template:
    metadata:
      labels:
        app: rabbitmq
    spec:
      containers:
        - name: rabbitmq
          image: rabbitmq:3.13-alpine
          ports:
            - containerPort: 5672
          resources:
            requests:
              cpu: "100m"
              memory: "128Mi"
            limits:
              cpu: "500m"
              memory: "256Mi"
---
apiVersion: v1
kind: Service
metadata:
  name: rabbitmq
spec:
  selector:
    app: rabbitmq
  ports:
    - port: 5672
      targetPort: 5672
EOF

# ─── 4. Secrets ──────────────────────────────────────────────────────────────
echo "==> [4/6] Creating task-service secret..."
kubectl create secret generic task-service-staging-task-service-secret \
  --namespace="$NAMESPACE" \
  --from-literal=postgres-connection-string="Host=postgres;Database=tasks;Username=app;Password=helios-demo-secret" \
  --dry-run=client -o yaml | kubectl apply -f -

# ─── 5. AnalysisTemplate ─────────────────────────────────────────────────────
echo "==> [5/6] Applying AnalysisTemplate..."
kubectl apply -n "$NAMESPACE" -f infra/rollouts/analysis-template.yaml

# ─── 6. Deploy stable version via Helm ───────────────────────────────────────
echo "==> [6/6] Deploying STABLE task-service (SHA: $STABLE_SHA)..."
helm upgrade --install task-service-staging infra/helm/task-service \
  --namespace "$NAMESPACE" \
  --values infra/helm/task-service/values.yaml \
  --values infra/helm/task-service/values-staging.yaml \
  --set image.tag="$STABLE_SHA" \
  --wait --timeout=120s

echo ""
echo "==> Staging environment ready!"
echo ""
echo "Next steps:"
echo "  1. Wait for pods to be Running:"
echo "     kubectl get pods -n $NAMESPACE -w"
echo ""
echo "  2. To start chaos demo, promote CHAOS build:"
echo "     helm upgrade task-service-staging infra/helm/task-service \\"
echo "       --namespace $NAMESPACE \\"
echo "       --values infra/helm/task-service/values.yaml \\"
echo "       --values infra/helm/task-service/values-staging.yaml \\"
echo "       --set image.tag=\"$CHAOS_SHA\" --reuse-values"
echo ""
echo "  3. Watch rollout:"
echo "     kubectl argo rollouts get rollout task-service-staging-task-service \\"
echo "       -n $NAMESPACE --watch"
echo ""
echo "  4. Deploy traffic generator:"
echo "     kubectl apply -f infra/chaos/traffic-generator.yaml"
