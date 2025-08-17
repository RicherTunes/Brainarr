#!/bin/bash

# Simple version bumping script for Unix/Linux systems

BUMP_TYPE=${1:-build}
VERSION_FILE="version.json"
PROPS_FILE="Directory.Build.props"
PLUGIN_FILE="plugin.json"

# Read current version
if [ ! -f "$VERSION_FILE" ]; then
    echo "Error: version.json not found!"
    exit 1
fi

CURRENT_VERSION=$(jq -r '.version' "$VERSION_FILE")
CURRENT_BUILD=$(jq -r '.buildNumber' "$VERSION_FILE")
SUFFIX=$(jq -r '.suffix' "$VERSION_FILE")

IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

echo "Current version: $MAJOR.$MINOR.$PATCH.$CURRENT_BUILD"

# Calculate new version
case "$BUMP_TYPE" in
    major)
        NEW_MAJOR=$((MAJOR + 1))
        NEW_MINOR=0
        NEW_PATCH=0
        NEW_BUILD=0
        ;;
    minor)
        NEW_MAJOR=$MAJOR
        NEW_MINOR=$((MINOR + 1))
        NEW_PATCH=0
        NEW_BUILD=0
        ;;
    patch)
        NEW_MAJOR=$MAJOR
        NEW_MINOR=$MINOR
        NEW_PATCH=$((PATCH + 1))
        NEW_BUILD=0
        ;;
    build)
        NEW_MAJOR=$MAJOR
        NEW_MINOR=$MINOR
        NEW_PATCH=$PATCH
        NEW_BUILD=$((CURRENT_BUILD + 1))
        ;;
    *)
        echo "Invalid bump type. Use: major, minor, patch, or build"
        exit 1
        ;;
esac

NEW_VERSION="$NEW_MAJOR.$NEW_MINOR.$NEW_PATCH"
FULL_VERSION="$NEW_MAJOR.$NEW_MINOR.$NEW_PATCH.$NEW_BUILD"

if [ -n "$SUFFIX" ] && [ "$SUFFIX" != "null" ]; then
    SEM_VERSION="$NEW_VERSION-$SUFFIX"
else
    SEM_VERSION="$NEW_VERSION"
fi

echo "New version: $FULL_VERSION"

# Update version.json
jq --arg v "$NEW_VERSION" --arg b "$NEW_BUILD" \
   '.version = $v | .buildNumber = ($b | tonumber)' \
   "$VERSION_FILE" > tmp.json && mv tmp.json "$VERSION_FILE"

# Update Directory.Build.props
if [ -f "$PROPS_FILE" ]; then
    sed -i.bak "s|<Version>.*</Version>|<Version>$SEM_VERSION</Version>|" "$PROPS_FILE"
    sed -i.bak "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$FULL_VERSION</AssemblyVersion>|" "$PROPS_FILE"
    sed -i.bak "s|<FileVersion>.*</FileVersion>|<FileVersion>$FULL_VERSION</FileVersion>|" "$PROPS_FILE"
    sed -i.bak "s|<InformationalVersion>.*</InformationalVersion>|<InformationalVersion>$SEM_VERSION</InformationalVersion>|" "$PROPS_FILE"
    rm "$PROPS_FILE.bak"
fi

# Update plugin.json
if [ -f "$PLUGIN_FILE" ]; then
    jq --arg v "$SEM_VERSION" '.version = $v' "$PLUGIN_FILE" > tmp.json && mv tmp.json "$PLUGIN_FILE"
    echo "Updated plugin.json"
fi

echo "Version updated successfully!"
echo ""
echo "Next steps:"
echo "1. Commit: git add -A && git commit -m 'chore: bump version to $SEM_VERSION'"
echo "2. Tag: git tag v$SEM_VERSION"
echo "3. Push: git push && git push --tags"

# For GitHub Actions
if [ -n "$GITHUB_OUTPUT" ]; then
    echo "version=$SEM_VERSION" >> "$GITHUB_OUTPUT"
    echo "full_version=$FULL_VERSION" >> "$GITHUB_OUTPUT"
fi