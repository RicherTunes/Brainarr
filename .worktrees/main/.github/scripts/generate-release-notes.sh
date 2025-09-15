#!/bin/bash

# Automated release notes generation for Brainarr plugin
# Generates comprehensive release notes from git history and CHANGELOG.md

set -e

VERSION="$1"
PREVIOUS_TAG="$2"

if [ -z "$VERSION" ]; then
    echo "❌ Usage: $0 <version> [previous_tag]"
    exit 1
fi

echo "🎯 Generating release notes for Brainarr $VERSION"

# Find previous tag if not provided
if [ -z "$PREVIOUS_TAG" ]; then
    PREVIOUS_TAG=$(git describe --tags --abbrev=0 HEAD~1 2>/dev/null || echo "")
fi

# Create release notes file
RELEASE_NOTES="release_notes.md"

cat > "$RELEASE_NOTES" << EOF
# 🧠 Brainarr $VERSION - AI-Powered Music Discovery

Thank you for using Brainarr! This release brings you the latest improvements to our AI-powered music recommendation engine.

## 📋 What's New

EOF

# Extract changes from CHANGELOG.md if it exists
if [ -f "CHANGELOG.md" ] && grep -q "## \[$VERSION\]" CHANGELOG.md; then
    echo "📝 Extracting changes from CHANGELOG.md..."

    # Extract the section for this version
    sed -n "/## \[$VERSION\]/,/## \[/p" CHANGELOG.md | head -n -1 | tail -n +2 >> "$RELEASE_NOTES"

else
    echo "🔍 Generating changes from git commits..."

    # Generate from commit history
    echo "### 🎉 Highlights" >> "$RELEASE_NOTES"
    echo "" >> "$RELEASE_NOTES"

    if [ -n "$PREVIOUS_TAG" ]; then
        COMMIT_RANGE="$PREVIOUS_TAG..HEAD"
        echo "📊 Analyzing commits from $PREVIOUS_TAG to HEAD"
    else
        COMMIT_RANGE="HEAD~20..HEAD"
        echo "📊 Analyzing recent 20 commits"
    fi

    # Features
    FEATURES=$(git log --pretty=format:"- %s" --no-merges "$COMMIT_RANGE" | grep -E "^- (feat|add|new)" | head -5)
    if [ -n "$FEATURES" ]; then
        echo "#### ✨ New Features" >> "$RELEASE_NOTES"
        echo "$FEATURES" >> "$RELEASE_NOTES"
        echo "" >> "$RELEASE_NOTES"
    fi

    # Improvements
    IMPROVEMENTS=$(git log --pretty=format:"- %s" --no-merges "$COMMIT_RANGE" | grep -E "^- (improve|enhance|update|refactor)" | head -5)
    if [ -n "$IMPROVEMENTS" ]; then
        echo "#### 🔧 Improvements" >> "$RELEASE_NOTES"
        echo "$IMPROVEMENTS" >> "$RELEASE_NOTES"
        echo "" >> "$RELEASE_NOTES"
    fi

    # Bug fixes
    FIXES=$(git log --pretty=format:"- %s" --no-merges "$COMMIT_RANGE" | grep -E "^- (fix|resolve|correct)" | head -5)
    if [ -n "$FIXES" ]; then
        echo "#### 🐛 Bug Fixes" >> "$RELEASE_NOTES"
        echo "$FIXES" >> "$RELEASE_NOTES"
        echo "" >> "$RELEASE_NOTES"
    fi
fi

# Add installation section
cat >> "$RELEASE_NOTES" << 'EOF'

## 🚀 Installation

### Easy Installation (Recommended)

1. **Open Lidarr Web Interface**
2. **Go to Settings → General → Updates**
3. **Set Branch to `nightly`** (plugins branch required)
4. **Go to Settings → Plugins**
5. **Click Add Plugin**
6. **Enter GitHub URL:** `https://github.com/RicherTunes/Brainarr`
7. **Click Install**
8. **Restart Lidarr when prompted**
9. **Navigate to Settings → Import Lists → Add New → Brainarr**

### Manual Installation

1. **Download `Brainarr-$VERSION.zip` from assets below**
2. **Extract to your Lidarr plugins directory:**
   - **Windows:** `C:\ProgramData\Lidarr\plugins\Brainarr\`
   - **Linux:** `/var/lib/lidarr/plugins/Brainarr/`
   - **Docker:** `/config/plugins/Brainarr/`
3. **Restart Lidarr**
4. **Configure in Settings → Import Lists**

## 🤖 Supported AI Providers

### 🏠 Privacy-First (Local)
- **Ollama** - 100% local, no data leaves your network
- **LM Studio** - Local with GUI interface

### 💰 Budget-Friendly
- **DeepSeek** - 10-20x cheaper than GPT-4
- **Google Gemini** - Free tier available
- **Groq** - 10x faster inference

### ⭐ Premium
- **OpenAI** - Industry-leading GPT-4o models
- **Anthropic** - Best reasoning with Claude 3.5/4 Sonnet
- **OpenRouter** - Access 200+ models with one API key
- **Perplexity** - Web-enhanced responses

## 🔍 Verification

Verify your download integrity:

```bash
# Check SHA256 hash
sha256sum -c Brainarr-$VERSION.zip.sha256

# Expected file structure after extraction:
Brainarr/
├── Lidarr.Plugin.Brainarr.dll    # Main plugin
├── plugin.json                    # Plugin manifest
├── README.md                      # Documentation
├── LICENSE                        # License file
└── dependencies/                  # NuGet packages
```

## 💬 Support

- **Documentation:** [README.md](https://github.com/RicherTunes/Brainarr/blob/main/README.md)
- **Issues:** [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues)
- **Discussions:** [GitHub Discussions](https://github.com/RicherTunes/Brainarr/discussions)

## 🏗️ For Developers

This release includes:
- ✅ Comprehensive test suite (33+ test files)
- ✅ Cross-platform CI/CD (Ubuntu/Windows/macOS)
- ✅ Docker-based assembly extraction for reliable builds
- ✅ Semantic versioning with automated releases

---

**Full Changelog:** [$PREVIOUS_TAG...$VERSION](https://github.com/RicherTunes/Brainarr/compare/$PREVIOUS_TAG...$VERSION)


EOF

# Add commit statistics if we have a previous tag
if [ -n "$PREVIOUS_TAG" ]; then
    echo "" >> "$RELEASE_NOTES"
    echo "## 📊 Release Statistics" >> "$RELEASE_NOTES"
    echo "" >> "$RELEASE_NOTES"
    echo "- **Commits:** $(git rev-list --count "$PREVIOUS_TAG..HEAD")" >> "$RELEASE_NOTES"
    echo "- **Files Changed:** $(git diff --name-only "$PREVIOUS_TAG..HEAD" | wc -l)" >> "$RELEASE_NOTES"
    echo "- **Contributors:** $(git log --format='%an' "$PREVIOUS_TAG..HEAD" | sort -u | wc -l)" >> "$RELEASE_NOTES"
fi

echo "✅ Release notes generated: $RELEASE_NOTES"
echo "📄 Preview:"
echo "----------------------------------------"
head -20 "$RELEASE_NOTES"
echo "... (truncated)"
echo "----------------------------------------"
