#!/bin/bash

echo "=== CI/CD Fix Application Script ==="
echo ""
echo "This script will help you apply the necessary CI workflow fixes."
echo ""
echo "Since GitHub Apps cannot modify workflow files, you need to apply these manually."
echo ""
echo "Steps to follow:"
echo ""
echo "1. First, ensure you're on the main branch and it's up to date:"
echo "   git checkout main"
echo "   git pull origin main"
echo ""
echo "2. Create a new branch for the fix:"
echo "   git checkout -b fix/ci-lidarr-runtime-paths"
echo ""
echo "3. Apply the patch file:"
echo "   git apply fix-lidarr-path.patch"
echo ""
echo "4. Or manually edit .github/workflows/ci.yml and replace ALL three occurrences"
echo "   of the 'Set Lidarr Path' step (around lines 60, 119, and 181) with:"
echo ""
cat << 'EOF'
    - name: Set Lidarr Path
      run: |
        if [ "${{ runner.os }}" == "Linux" ]; then
          echo "LIDARR_PATH=${{ github.workspace }}/ext/Lidarr/_output/net6.0/linux-x64" >> $GITHUB_ENV
        elif [ "${{ runner.os }}" == "Windows" ]; then
          echo "LIDARR_PATH=${{ github.workspace }}/ext/Lidarr/_output/net6.0/win-x64" >> $GITHUB_ENV
        else
          echo "LIDARR_PATH=${{ github.workspace }}/ext/Lidarr/_output/net6.0/osx-x64" >> $GITHUB_ENV
        fi
      shell: bash
EOF
echo ""
echo "5. Commit the changes:"
echo "   git add .github/workflows/ci.yml"
echo "   git commit -m 'fix: update CI to use platform-specific Lidarr output paths'"
echo ""
echo "6. Push the branch:"
echo "   git push origin fix/ci-lidarr-runtime-paths"
echo ""
echo "7. Create a PR from this branch to main"
echo ""
echo "8. Once the CI passes, merge the PR"
echo ""
echo "This fix addresses the issue where Lidarr builds to platform-specific directories"
echo "(linux-x64, win-x64, osx-x64) but the CI was looking in the wrong location."