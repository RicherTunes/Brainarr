#!/bin/bash

# Interactive Release Script for Brainarr
# Facilitates creating new releases with proper versioning and automation

set -e

echo "🧠 Brainarr Release Manager"
echo "=========================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check prerequisites
echo -e "${BLUE}🔍 Checking prerequisites...${NC}"

# Check if we're in the right directory
if [[ ! -f "plugin.json" ]] || [[ ! -d "Brainarr.Plugin" ]]; then
    echo -e "${RED}❌ Error: Please run this script from the Brainarr root directory${NC}"
    exit 1
fi

# Check for uncommitted changes
if ! git diff --quiet || ! git diff --cached --quiet; then
    echo -e "${RED}❌ Error: You have uncommitted changes. Please commit or stash them first.${NC}"
    git status --porcelain
    exit 1
fi

# Check if on main branch
current_branch=$(git branch --show-current)
if [[ "$current_branch" != "main" ]]; then
    echo -e "${YELLOW}⚠️ Warning: You're on branch '$current_branch', not 'main'${NC}"
    read -p "Continue anyway? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        exit 1
    fi
fi

# Check authentication
if ! gh auth status &>/dev/null; then
    echo -e "${YELLOW}🔐 GitHub CLI not authenticated. Running gh auth login...${NC}"
    gh auth login
fi

echo -e "${GREEN}✅ Prerequisites check passed${NC}"
echo ""

# Get current version
current_version=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
echo -e "${BLUE}📋 Current version: $current_version${NC}"

# Get version from user
echo ""
echo -e "${BLUE}📝 Version Selection:${NC}"
echo "  1. Patch (bug fixes): ${current_version} → $(echo $current_version | awk -F. '{print $1"."$2"."($3+1)}' | sed 's/v/v/')"
echo "  2. Minor (new features): ${current_version} → $(echo $current_version | awk -F. '{print $1"."($2+1)".0"}' | sed 's/v/v/')"
echo "  3. Major (breaking changes): ${current_version} → $(echo $current_version | awk -F. '{print "v"($1+1)".0.0"}' | sed 's/vv/v/')"
echo "  4. Custom version"
echo "  5. Prerelease (alpha/beta/rc)"

read -p "Select version type (1-5): " version_type

case $version_type in
    1)
        new_version=$(echo $current_version | awk -F. '{print $1"."$2"."($3+1)}' | sed 's/v/v/')
        release_type="patch"
        ;;
    2)
        new_version=$(echo $current_version | awk -F. '{print $1"."($2+1)".0"}' | sed 's/v/v/')
        release_type="minor"
        ;;
    3)
        new_version=$(echo $current_version | awk -F. '{print "v"($1+1)".0.0"}' | sed 's/vv/v/')
        release_type="major"
        ;;
    4)
        read -p "Enter custom version (e.g., v1.2.3): " new_version
        if [[ ! $new_version =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
            echo -e "${RED}❌ Invalid version format. Use: v1.2.3${NC}"
            exit 1
        fi
        release_type="custom"
        ;;
    5)
        read -p "Enter prerelease version (e.g., v1.2.3-beta.1): " new_version
        if [[ ! $new_version =~ ^v[0-9]+\.[0-9]+\.[0-9]+-(alpha|beta|rc)\.[0-9]+$ ]]; then
            echo -e "${RED}❌ Invalid prerelease format. Use: v1.2.3-beta.1${NC}"
            exit 1
        fi
        release_type="prerelease"
        ;;
    *)
        echo -e "${RED}❌ Invalid selection${NC}"
        exit 1
        ;;
esac

echo ""
echo -e "${GREEN}🎯 Selected version: $new_version ($release_type)${NC}"

# Confirm release
echo ""
echo -e "${YELLOW}🚀 Release Summary:${NC}"
echo "  Current: $current_version"
echo "  New: $new_version"
echo "  Type: $release_type"
echo "  Branch: $current_branch"
echo ""

read -p "Create this release? (y/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "❌ Release cancelled"
    exit 1
fi

echo ""
echo -e "${BLUE}🔧 Preparing release $new_version...${NC}"

# Check if tag already exists
if git rev-parse "$new_version" >/dev/null 2>&1; then
    echo -e "${RED}❌ Tag $new_version already exists${NC}"
    read -p "Delete existing tag and recreate? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo "🗑️ Deleting existing tag..."
        git tag -d "$new_version"
        git push origin ":refs/tags/$new_version" 2>/dev/null || true
    else
        exit 1
    fi
fi

# Run tests first
echo -e "${BLUE}🧪 Running tests before release...${NC}"
if ! dotnet test --configuration Release --logger:"console;verbosity=minimal" --filter "Category=Unit|Category=Provider|Category=EdgeCase|Category=Integration|Category=BugFix|Category=Security|Category=Resilience|Category=Critical" >/dev/null 2>&1; then
    echo -e "${RED}❌ Tests failed! Cannot create release with failing tests.${NC}"
    echo "Run 'dotnet test' to see failures and fix before releasing."
    exit 1
fi
echo -e "${GREEN}✅ Tests passed${NC}"

# Generate changelog
echo -e "${BLUE}📝 Generating changelog...${NC}"
if [[ "$current_version" != "v0.0.0" ]]; then
    commits=$(git log --pretty=format:"• %s" "$current_version"..HEAD)
    if [[ -z "$commits" ]]; then
        commits="• Minor improvements and bug fixes"
    fi
else
    commits="• Initial release"
fi

# Create release notes
release_notes="Release $new_version

## What's New

$commits

## Installation

### Docker (Recommended)
\`\`\`yaml
image: ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
\`\`\`

### Manual Installation
1. Download Brainarr-$new_version.zip
2. Extract to Lidarr plugins directory
3. Restart Lidarr
4. Configure in Settings → Import Lists

## Supported AI Providers
🏠 Local: Ollama, LM Studio
☁️ Cloud: OpenAI, Anthropic, Gemini, OpenRouter, Groq, DeepSeek, Perplexity

## Verification
\`\`\`bash
sha256sum -c Brainarr-$new_version.zip.sha256
\`\`\`

Full documentation: https://github.com/RicherTunes/Brainarr/wiki"

echo ""
echo -e "${BLUE}📋 Release Notes Preview:${NC}"
echo "----------------------------------------"
echo "$release_notes" | head -15
echo "----------------------------------------"
echo ""

# Create and push tag
echo -e "${BLUE}🏷️ Creating tag $new_version...${NC}"
git tag -a "$new_version" -m "Release $new_version

$commits

🤖 Generated with Brainarr Release Manager"

echo -e "${BLUE}📤 Pushing tag to trigger release automation...${NC}"
git push origin "$new_version"

echo ""
echo -e "${GREEN}🎉 Release $new_version created successfully!${NC}"
echo ""
echo -e "${BLUE}🚀 Automation Status:${NC}"
echo "  ✅ Tag pushed to GitHub"
echo "  🔄 GitHub Actions building release..."
echo "  📚 Wiki will auto-update with new version"
echo "  📦 Release packages will be built automatically"
echo ""

# Monitor release progress
echo -e "${BLUE}📊 Monitoring release progress...${NC}"
echo "🔗 Release URL: https://github.com/RicherTunes/Brainarr/releases/tag/$new_version"
echo "🔗 Actions: https://github.com/RicherTunes/Brainarr/actions"
echo ""

# Wait a moment for GitHub to register the tag
sleep 3

# Check if release workflow started
echo "⏳ Checking for running workflows..."
if gh run list --event push --limit 3 --json status,name,createdAt | grep -q "in_progress\|queued"; then
    echo -e "${GREEN}✅ Release automation started!${NC}"
    echo ""
    echo "📋 What happens next:"
    echo "  1. 🔨 Plugin builds on 6 platform combinations"
    echo "  2. 🧪 Test suite runs (485 tests)"
    echo "  3. 🔒 Security scan completes"
    echo "  4. 📦 Release packages are created"  
    echo "  5. 📚 Wiki updates with new version"
    echo "  6. 🎁 GitHub release published"
    echo ""
    echo "⏱️  Total time: ~5-10 minutes"
    echo ""
    
    # Optional: Wait for completion
    read -p "Monitor release progress? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo "👀 Monitoring release progress (Ctrl+C to exit)..."
        while true; do
            status=$(gh run list --event push --limit 1 --json status,conclusion,name | jq -r '.[0] | "\(.status) - \(.name)"' 2>/dev/null || echo "checking...")
            echo "📊 Status: $status"
            
            if [[ "$status" == *"completed"* ]]; then
                conclusion=$(gh run list --event push --limit 1 --json conclusion | jq -r '.[0].conclusion' 2>/dev/null)
                if [[ "$conclusion" == "success" ]]; then
                    echo -e "${GREEN}🎉 Release $new_version completed successfully!${NC}"
                    echo "🔗 View release: https://github.com/RicherTunes/Brainarr/releases/tag/$new_version"
                    break
                else
                    echo -e "${RED}❌ Release failed with status: $conclusion${NC}"
                    echo "🔗 Check logs: https://github.com/RicherTunes/Brainarr/actions"
                    break
                fi
            fi
            
            sleep 10
        done
    fi
else
    echo -e "${YELLOW}⏳ Release automation may take a moment to start...${NC}"
fi

echo ""
echo -e "${GREEN}🎯 Release $new_version initiated!${NC}"
echo -e "${BLUE}📖 Check progress at: https://github.com/RicherTunes/Brainarr/actions${NC}"
echo ""
echo "🎵 Happy music discovering! 🎵"