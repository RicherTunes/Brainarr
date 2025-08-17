#!/bin/bash

echo "=========================================="
echo "Brainarr Complete Branch Cleanup"
echo "=========================================="
echo ""
echo "This will delete ALL branches except main"
echo ""

# List of all branches to delete
BRANCHES=(
  "RicherTunes-patch-1"
  "RicherTunes-patch-2"
  "add-tasks-documentation-3b67ed9"
  "ci-formatting-fixes-f8e07e7"
  "complete-ci-fixes-57e7224"
  "critical-build-fix-ea058b5"
  "critical-improvements-31a4ea1"
  "dependabot/github_actions/actions/checkout-5"
  "dependabot/github_actions/codecov/codecov-action-5"
  "dependabot/github_actions/peter-evans/create-pull-request-7"
  "dependabot/nuget/FluentValidation-11.11.0"
  "dependabot/nuget/Microsoft.Extensions.Caching.Memory-9.0.8"
  "dependabot/nuget/NLog-6.0.3"
  "feat/git-submodule-optimization"
  "final-ci-fixes-0d89ec2"
  "fix-ci-real-assemblies"
  "fix-ci-script-paths"
  "fix-ci-stub-assemblies"
  "fix-ci-submodule-plugins"
  "fix-compilation-errors-1755311913"
  "fix-workflow-conflicts"
  "fix-workflows"
  "fix/ci-action-versions-final"
  "fix/ci-build-failures"
  "fix/ci-cross-platform-paths"
  "fix/ci-submodule-final"
  "fix/complete-ci-assembly-fix"
  "fix/comprehensive-ci-actions-update"
  "fix/comprehensive-ci-final-solution"
  "fix/critical-ci-build-issues"
  "fix/final-lidarr-build-solution"
  "fix/individual-project-builds-msbuild"
  "fix/msbuild-lidarr-path-property"
  "hotfix-ci-workflows"
  "production-v1.0.0"
  "solution-build-fix-869c056"
  "terragon/audit-code-security-performance"
  "terragon/audit-improve-bulletproof-code"
  "terragon/autonomous-tech-debt-framework"
  "terragon/autonomous-tech-debt-refactor"
  "terragon/complete-ci-fix"
  "terragon/complete-submodule-fix"
  "terragon/doc-audit-enhancement"
  "terragon/doc-audit-enhancement-v53srw"
  "terragon/fix-ci-workflow"
  "terragon/fix-github-build-failure"
  "terragon/fix-submodule-gitignore"
  "terragon/update-lidarr-plugin-docs"
  "update-dependencies"
  "workflow-final-fixes-4425797"
  "workflow-final-fixes-abaed68"
  "workflow-final-fixes-cca1558"
)

echo "Found ${#BRANCHES[@]} branches to delete"
echo ""
echo "Keep this branch for now (has the CI fix):"
echo "  - fix/remove-conflicting-workflows"
echo ""
read -p "Delete all ${#BRANCHES[@]} branches? (y/N): " confirm

if [ "$confirm" = "y" ] || [ "$confirm" = "Y" ]; then
    echo ""
    echo "Deleting branches..."
    echo ""
    
    for branch in "${BRANCHES[@]}"; do
        echo -n "Deleting $branch... "
        if git push origin --delete "$branch" 2>/dev/null; then
            echo "✅ deleted"
        else
            echo "⚠️ already deleted or protected"
        fi
    done
    
    echo ""
    echo "Cleaning up local references..."
    git remote prune origin
    
    echo ""
    echo "✅ Cleanup complete!"
    echo ""
    echo "Remaining remote branches:"
    git branch -r
else
    echo "Cleanup cancelled."
fi