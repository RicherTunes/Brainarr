# TODO: Restore project auto-add workflows

**Context**
- Both `.github/workflows/project-auto-add.yml` and `.github/workflows/add-to-project.yml` were disabled on 2025-09-16 because they rely on `secrets.PROJECTS_TOKEN`.
- The repo currently lacks that PAT or the necessary project-write scope, so the workflows failed whenever triggered.

**What Needs To Happen**
1. Create a PAT (classic) with `project` and `repo` scopes under RicherTunes (or move to a GitHub App) and store it as `PROJECTS_TOKEN` in the repo/org secrets.
2. Set `vars.PROJECT_URL` if we want the generic workflow to remain configurable.
3. Re-enable the workflows by renaming
   - `.github/workflows/project-auto-add.yml.disabled` → `.yml`
   - `.github/workflows/add-to-project.yml.disabled` → `.yml`
4. Run the workflow manually to verify cards get added to Project #1 without failures.

**Owner Suggestion**
Assign to the DevOps/Infra rotation.
