#!/bin/bash
# 🚀 Brainarr Version Bump Script
# Usage: ./scripts/bump-version.sh patch|minor|major [--dry-run]

set -e

BUMP_TYPE=$1
DRY_RUN=$2

if [ -z "$BUMP_TYPE" ]; then
    echo "❌ Usage: $0 patch|minor|major [--dry-run]"
    echo "   patch: 1.0.0 -> 1.0.1 (bug fixes)"
    echo "   minor: 1.0.0 -> 1.1.0 (new features)"
    echo "   major: 1.0.0 -> 2.0.0 (breaking changes)"
    exit 1
fi

if [[ ! "$BUMP_TYPE" =~ ^(patch|minor|major)$ ]]; then
    echo "❌ Invalid bump type: $BUMP_TYPE"
    echo "   Valid options: patch, minor, major"
    exit 1
fi

# Get current version from plugin.json
get_current_version() {
    jq -r '.version' plugin.json
}

# Calculate new version
calculate_new_version() {
    local current=$1
    local bump_type=$2
    
    IFS='.' read -ra VERSION_PARTS <<< "$current"
    local major=${VERSION_PARTS[0]}
    local minor=${VERSION_PARTS[1]}
    local patch=${VERSION_PARTS[2]}
    
    case $bump_type in
        "major")
            major=$((major + 1))
            minor=0
            patch=0
            ;;
        "minor")
            minor=$((minor + 1))
            patch=0
            ;;
        "patch")
            patch=$((patch + 1))
            ;;
    esac
    
    echo "$major.$minor.$patch"
}

# Update files with new version
update_files() {
    local new_version=$1
    
    # Update plugin.json
    jq --arg version "$new_version" '.version = $version' plugin.json > plugin.json.tmp
    mv plugin.json.tmp plugin.json
    
    # Update README.md version badge
    sed -i.bak "s/version-[0-9]\+\.[0-9]\+\.[0-9]\+-brightgreen/version-$new_version-brightgreen/g" README.md
    rm -f README.md.bak
    
    echo "✅ Updated plugin.json and README.md to version $new_version"
}

# Main script
main() {
    local current_version
    local new_version
    
    # Check if we're in the right directory
    if [ ! -f "plugin.json" ]; then
        echo "❌ plugin.json not found. Are you in the Brainarr root directory?"
        exit 1
    fi
    
    current_version=$(get_current_version)
    new_version=$(calculate_new_version "$current_version" "$BUMP_TYPE")
    
    echo "🏷️ Version Bump: $BUMP_TYPE"
    echo "📋 Current Version: $current_version"
    echo "🚀 New Version: $new_version"
    
    if [ "$DRY_RUN" = "--dry-run" ]; then
        echo "🔍 DRY RUN - No files will be modified"
        exit 0
    fi
    
    # Confirm with user
    echo -n "Continue with version bump? (y/N): "
    read -r confirm
    if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
        echo "❌ Version bump cancelled"
        exit 1
    fi
    
    # Update files
    update_files "$new_version"
    
    echo ""
    echo "✅ Version bump complete!"
    echo "📝 Next steps:"
    echo "   1. Review changes: git diff"
    echo "   2. Commit changes: git add . && git commit -m 'chore: bump version to $new_version'"
    echo "   3. Create release: Go to GitHub Actions → Release Management → Run workflow"
}

main