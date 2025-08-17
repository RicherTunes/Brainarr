# URGENT: CI Build is Broken - Manual Fix Required

The CI builds are failing because the workflow doesn't build Lidarr from source first.

## To Fix Immediately:

1. Copy the fixed workflow:
```bash
cp workflow-templates/ci-with-lidarr-build.yml .github/workflows/ci.yml
```

2. Commit and push:
```bash
git add .github/workflows/ci.yml
git commit -m "fix: add Lidarr build steps to CI workflow"
git push origin terragon/review-build-fix-tech-debt
```

## Why This Is Needed

The plugin requires Lidarr assemblies to compile. The workflow needs to:
1. Install Node.js and Yarn
2. Build Lidarr from the submodule
3. Set LIDARR_PATH environment variable

GitHub Apps cannot modify workflow files, so you must apply this fix manually.