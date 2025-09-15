#!/bin/bash
# Install git hooks for Brainarr project

HOOKS_DIR="$(dirname "$0")"
GIT_DIR="$(git rev-parse --git-dir 2>/dev/null)"

if [ -z "$GIT_DIR" ]; then
    echo "❌ Not in a git repository"
    exit 1
fi

echo "📦 Installing git hooks..."

# Create hooks directory if it doesn't exist
mkdir -p "$GIT_DIR/hooks"

# Install pre-commit hook
if [ -f "$HOOKS_DIR/pre-commit" ]; then
    cp "$HOOKS_DIR/pre-commit" "$GIT_DIR/hooks/pre-commit"
    chmod +x "$GIT_DIR/hooks/pre-commit"
    echo "✅ Pre-commit hook installed"
else
    echo "⚠️  Pre-commit hook not found in $HOOKS_DIR"
fi

echo "✅ Git hooks installation complete!"
echo ""
echo "To bypass hooks temporarily, use: git commit --no-verify"
