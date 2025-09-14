#!/bin/bash

# Quick release script for Brainarr plugin
# Automates the entire release process: version bump, commit, tag, and push

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

cd "$PROJECT_ROOT"

echo "🧠 Brainarr Quick Release Script"
echo "==============================="

# Check if we're on main branch
CURRENT_BRANCH=$(git branch --show-current)
if [ "$CURRENT_BRANCH" != "main" ]; then
    echo "⚠️  Warning: You're on branch '$CURRENT_BRANCH', not 'main'"
    read -p "Continue anyway? [y/N] " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "❌ Aborted"
        exit 1
    fi
fi

# Check for uncommitted changes
if ! git diff-index --quiet HEAD --; then
    echo "⚠️  You have uncommitted changes:"
    git status --porcelain
    read -p "Stash changes and continue? [y/N] " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        git stash push -m "Pre-release stash $(date)"
        echo "📦 Changes stashed"
    else
        echo "❌ Please commit or stash your changes first"
        exit 1
    fi
fi

# Get current version
CURRENT_VERSION=$(grep '"version"' plugin.json | sed 's/.*"version": "\(.*\)".*/\1/')
echo "📋 Current version: $CURRENT_VERSION"

# Determine bump type
echo ""
echo "Select version bump type:"
echo "1) patch (1.0.0 → 1.0.1) - Bug fixes"
echo "2) minor (1.0.0 → 1.1.0) - New features"
echo "3) major (1.0.0 → 2.0.0) - Breaking changes"
echo "4) Custom version"
echo "5) Auto-detect from commits"

read -p "Choice [1-5]: " -n 1 -r BUMP_CHOICE
echo

case $BUMP_CHOICE in
    1)
        BUMP_TYPE="patch"
        ;;
    2)
        BUMP_TYPE="minor"
        ;;
    3)
        BUMP_TYPE="major"
        ;;
    4)
        read -p "Enter version (e.g., 1.2.0 or 1.2.0-beta.1): " CUSTOM_VERSION
        BUMP_TYPE="custom"
        ;;
    5)
        BUMP_TYPE="auto"
        ;;
    *)
        echo "❌ Invalid choice"
        exit 1
        ;;
esac

# Run version bump script
echo "🔄 Bumping version..."
if [ "$BUMP_TYPE" = "custom" ]; then
    pwsh .github/scripts/bump-version.ps1 -Version "$CUSTOM_VERSION"
    NEW_VERSION="$CUSTOM_VERSION"
else
    pwsh .github/scripts/bump-version.ps1 -BumpType "$BUMP_TYPE"
    NEW_VERSION=$(grep '"version"' plugin.json | sed 's/.*"version": "\(.*\)".*/\1/')
fi

echo "✅ Version bumped to: $NEW_VERSION"

# Show changes
echo ""
echo "📄 Files changed:"
git diff --name-only

# Confirmation
echo ""
read -p "Create release v$NEW_VERSION? [y/N] " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "❌ Release cancelled"
    echo "💡 To undo version changes: git checkout -- ."
    exit 0
fi

# Commit changes
echo "📝 Committing version bump..."
git add .
git commit -m "chore: bump version to $NEW_VERSION

🚀 Automated version bump for release

- Updated plugin.json: $CURRENT_VERSION → $NEW_VERSION
- Updated project files with new version numbers
- Ready for release automation

🤖 Generated with quick-release script"

# Create and push tag
echo "🏷️  Creating and pushing tag..."
git tag "v$NEW_VERSION"
git push origin "$CURRENT_BRANCH"
git push origin "v$NEW_VERSION"

echo ""
echo "🎉 Release v$NEW_VERSION initiated!"
echo "🔗 GitHub Actions: https://github.com/$(git remote get-url origin | sed 's|https://github.com/||' | sed 's|\.git||')/actions"
echo "📦 Release will be available at: https://github.com/$(git remote get-url origin | sed 's|https://github.com/||' | sed 's|\.git||')/releases/tag/v$NEW_VERSION"
echo ""
echo "📊 Monitor progress:"
echo "   gh run watch"
echo ""
echo "🎯 Next steps:"
echo "   1. GitHub Actions will build and test the plugin"
echo "   2. Release notes will be auto-generated"
echo "   3. Plugin package will be created and uploaded"
echo "   4. Release will be published automatically"
echo ""
echo "⏱️  ETA: ~5-10 minutes for full release"
