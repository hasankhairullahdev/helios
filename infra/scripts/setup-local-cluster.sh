#!/usr/bin/env bash
# =============================================================================
# setup-local-cluster.sh
# Bootstrap seluruh local dev environment dari nol:
#   1. Buat kind cluster
#   2. Install ArgoCD
#   3. Install Argo Rollouts
#   4. Apply App-of-Apps root manifest
#
# Prerequisite: kind, kubectl, helm sudah terinstall.
# Jalankan sekali saja — idempotent kalau dijalankan ulang.
# =============================================================================
set -euo pipefail

CLUSTER_NAME="helios"
ARGOCD_VERSION="v2.14.6"
ARGO_ROLLOUTS_VERSION="v1.8.3"

echo "=== [1/5] Creating kind cluster: $CLUSTER_NAME ==="
# Cek kalau cluster sudah ada — skip kalau sudah
if kind get clusters 2>/dev/null | grep -q "^${CLUSTER_NAME}$"; then
  echo "Cluster '$CLUSTER_NAME' already exists, skipping."
else
  # extraPortMappings: expose port 8080 ke host supaya bisa curl dari luar cluster
  cat <<EOF | kind create cluster --name "$CLUSTER_NAME" --config=-
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
  - role: control-plane
    extraPortMappings:
      - containerPort: 30080
        hostPort: 8080
        protocol: TCP
EOF
fi

echo ""
echo "=== [2/5] Installing ArgoCD $ARGOCD_VERSION ==="
kubectl create namespace argocd --dry-run=client -o yaml | kubectl apply -f -
kubectl apply -n argocd \
  -f "https://raw.githubusercontent.com/argoproj/argo-cd/${ARGOCD_VERSION}/manifests/install.yaml"

echo ""
echo "=== [3/5] Waiting for ArgoCD to be ready ==="
kubectl rollout status deployment/argocd-server -n argocd --timeout=120s

echo ""
echo "=== [4/5] Installing Argo Rollouts $ARGO_ROLLOUTS_VERSION ==="
kubectl create namespace argo-rollouts --dry-run=client -o yaml | kubectl apply -f -
kubectl apply -n argo-rollouts \
  -f "https://github.com/argoproj/argo-rollouts/releases/download/${ARGO_ROLLOUTS_VERSION}/install.yaml"

echo ""
echo "=== [5/5] Applying App-of-Apps root manifest ==="
# CATATAN: Edit repoURL di infra/argocd/app-of-apps.yaml sebelum jalankan ini!
kubectl apply -f infra/argocd/app-of-apps.yaml

echo ""
echo "================================================================="
echo "✅ Local cluster ready!"
echo ""
echo "ArgoCD UI:"
echo "  kubectl port-forward svc/argocd-server -n argocd 8080:443"
echo "  Open: https://localhost:8080"
echo ""
echo "ArgoCD initial password:"
echo "  kubectl get secret argocd-initial-admin-secret -n argocd \\"
echo "    -o jsonpath='{.data.password}' | base64 -d"
echo ""
echo "Watch rollout (Phase 6):"
echo "  kubectl argo rollouts get rollout task-service -n helios-staging --watch"
echo "================================================================="
