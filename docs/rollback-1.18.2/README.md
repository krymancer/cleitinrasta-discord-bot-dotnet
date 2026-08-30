# Rollback snapshots: YouTube plugin 1.18.2

These files are the `k8s/` YouTube stack as of `4ee3472` (static poToken +
bgutil CronJob). They are **not** live manifests. Argo only syncs `k8s/`.

Preferred one-step rollback after the remote-PoT change is on `main`:

```bash
git revert --no-edit <merge-commit-sha>
git push origin main
```

Then sync Argo CD app `discord-bot`.
