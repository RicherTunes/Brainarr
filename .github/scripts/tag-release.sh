#!/bin/bash

# Simple tag-based release for Brainarr
# Just creates a tag - everything else is automated

set -e

if [ $# -eq 0 ]; then
    echo "🧠 Brainarr Tag Release"
    echo "Usage: $0 <version>"
    echo ""
    echo "Examples:"
    echo "  $0 1.2.0        # Stable release"
    echo "  $0 1.2.0-beta.1 # Beta release"
    echo "  $0 1.2.0-alpha.1# Alpha release"
    echo ""
    echo "Current version: $(grep '"version"' plugin.json | sed 's/.*"version": "\(.*\)".*/\1/')"
    exit 1
fi

VERSION="$1"

# Validate version format
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
    echo "❌ Invalid version format: $VERSION"
    echo "Expected format: X.Y.Z or X.Y.Z-prerelease"
    exit 1
fi

# Check if tag already exists
if git rev-parse "v$VERSION" >/dev/null 2>&1; then
    echo "❌ Tag v$VERSION already exists"
    exit 1
fi

# Check for uncommitted changes
if ! git diff-index --quiet HEAD --; then
    echo "⚠️  You have uncommitted changes. Please commit or stash them first."
    exit 1
fi

echo "🏷️  Creating tag v$VERSION..."

# Create annotated tag with message
git tag -a "v$VERSION" -m "Release v$VERSION

🚀 Automated release via tag

This tag triggers the full release automation:
- Version number updates
- Plugin build and testing  
- Release notes generation
- Package creation and upload
- GitHub release publication

Generated: $(date -u +'%Y-%m-%d %H:%M:%S UTC')
Commit: $(git rev-parse --short HEAD)"

echo "📤 Pushing tag..."
git push origin "v$VERSION"

echo ""
echo "🎉 Tag v$VERSION created and pushed!"
echo ""
echo "🔗 Monitor release progress:"
echo "   GitHub Actions: https://github.com/$(git remote get-url origin | sed 's|https://github.com/||' | sed 's|\.git||')/actions"
echo "   Release will appear at: https://github.com/$(git remote get-url origin | sed 's|https://github.com/||' | sed 's|\.git||')/releases"
echo ""
echo "🤖 Automated release process will:"
echo "   ✅ Update version numbers in all files"
echo "   ✅ Build and test the plugin"
echo "   ✅ Generate comprehensive release notes"
echo "   ✅ Create plugin package with checksums"
echo "   ✅ Publish GitHub release with assets"
echo ""
echo "⏱️  ETA: 5-10 minutes for complete release"