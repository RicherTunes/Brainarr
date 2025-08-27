#!/bin/bash

# Upload Brainarr Wiki Content
# This script uploads all wiki pages to GitHub Wiki

set -e  # Exit on any error

echo "🧠 Brainarr Wiki Upload Script"
echo "================================"

# Check if we're in the right directory
if [[ ! -d "Brainarr.Plugin" ]]; then
    echo "❌ Error: Please run this script from the Brainarr root directory"
    exit 1
fi

# Check if wiki content exists
if [[ ! -d "wiki-content" ]]; then
    echo "❌ Error: wiki-content directory not found"
    exit 1
fi

echo "📋 Available wiki pages:"
ls wiki-content/*.md | xargs -n1 basename | sed 's/\.md$//'

echo ""
echo "🔧 Step 1: Create first wiki page manually"
echo "  1. Go to: https://github.com/RicherTunes/Brainarr/wiki"
echo "  2. Click 'Create the first page'"  
echo "  3. Copy content from wiki-content/Home.md"
echo "  4. Save as 'Home'"
echo ""
echo "⏳ Waiting for first page creation..."
echo "Press Enter when you've created the Home page..."
read -r

echo ""
echo "📥 Step 2: Clone wiki repository"
cd ..
if [[ -d "Brainarr.wiki" ]]; then
    echo "🔄 Wiki directory exists, pulling latest..."
    cd Brainarr.wiki
    git pull origin master
else
    echo "📦 Cloning wiki repository..."
    git clone https://github.com/RicherTunes/Brainarr.wiki.git
    cd Brainarr.wiki
fi

echo ""
echo "📝 Step 3: Copy wiki content"
cp ../Brainarr/wiki-content/*.md .

# Convert file names to wiki format (replace hyphens with spaces)
for file in *.md; do
    if [[ "$file" == *"-"* ]]; then
        new_name=$(echo "$file" | sed 's/-/ /g')
        mv "$file" "$new_name"
        echo "📄 Renamed: $file → $new_name"
    fi
done

echo ""
echo "📊 Wiki pages to upload:"
ls -la *.md

echo ""
echo "🚀 Step 4: Upload to GitHub"
git add .
git commit -m "feat: comprehensive Brainarr wiki documentation

• Complete installation guide with Docker and manual setup
• Detailed provider setup for all 9 AI providers  
• Troubleshooting guide with actual error scenarios
• First-run guide for optimal initial experience
• Local and cloud provider comparison and recommendations

Based on actual codebase analysis for 100% accuracy."

git push origin master

echo ""
echo "✅ Wiki upload complete!"
echo "🔗 View at: https://github.com/RicherTunes/Brainarr/wiki"
echo ""
echo "📖 Pages uploaded:"
ls *.md | sed 's/\.md$//' | sed 's/^/  • /'

cd ../Brainarr