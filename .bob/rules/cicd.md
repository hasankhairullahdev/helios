# Rules: CI/CD (GitHub Actions)

Scope: `.github/workflows/`

## Pipeline stages (in order, fail-fast)
1. **Build** — `dotnet restore` + `dotnet build`, matrix over both services so
   they build independently and in parallel.
2. **Test** — `dotnet test`, publish results as a check annotation.
3. **SAST / image scan** — Trivy scan against the built image (filesystem
   scan pre-build is also fine as a fast first pass); fail the pipeline on
   HIGH/CRITICAL findings. CodeQL as a separate parallel job is a nice stretch
   addition but not required for the core pipeline.
4. **Build & push image** — only runs after build+test+scan pass. Tag with
   `ghcr.io/<org>/<service>:<git-sha-short>`. Also push a `:branch-name`
   floating tag for convenience, but the deployment manifests only ever
   reference the SHA tag.
5. **Bump Helm values** — the CI job updates `image.tag` in
   `infra/helm/<service>/values-dev.yaml` (dev only — staging/prod promotion
   is a separate, deliberate step, see below) and commits that change back to
   the repo. This commit is what ArgoCD picks up — CI never talks to the
   cluster directly.

## Environment promotion model
- **dev**: auto-promoted on every merge to `main` (CI bumps `values-dev.yaml`
  automatically, as above).
- **staging**: promoted via a manual GitHub Actions `workflow_dispatch` that
  copies the known-good SHA tag from dev's values file into
  `values-staging.yaml` and commits.
- **prod**: promoted the same way as staging, but requires a GitHub
  Environment protection rule (manual approval/reviewer) before the workflow
  is allowed to run.
- This is the core "GitOps" discipline: CI never deploys anything itself, it
  only ever changes a Git file. ArgoCD is the only thing that talks to the
  cluster.

## Caching & performance
- Cache NuGet packages keyed on the lockfile hash.
- Use Docker layer caching (`cache-from`/`cache-to` with GHA cache backend)
  so repeated builds of an unchanged service are fast.
- Matrix build both services in parallel jobs, not sequentially.

## Secrets used in workflows
- `GITHUB_TOKEN` (GHCR push) — default, no extra setup.
- Nothing else should be needed for CI itself; runtime secrets are handled by
  the cluster (see `argocd-rollouts.md` / stretch: External Secrets), never
  injected via GitHub Actions secrets into manifests.

## Branch strategy
- Trunk-based: short-lived feature branches → PR → squash-merge to `main`.
- No long-lived `develop`/`release` branches — environment state lives in
  Git via the values files, not via branches.
