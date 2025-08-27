# Setup GitHub Wiki for Brainarr
# This script helps upload local wiki content to GitHub Wiki

Write-Host "🧠 Brainarr Wiki Setup Script" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Check if we're in the right directory
if (-not (Test-Path "Brainarr.Plugin")) {
    Write-Error "Please run this script from the Brainarr root directory"
    exit 1
}

# Check if wiki content exists
if (-not (Test-Path "wiki-content")) {
    Write-Error "Wiki content directory not found. Please create wiki pages first."
    exit 1
}

Write-Host "📋 Available wiki pages:" -ForegroundColor Green
Get-ChildItem "wiki-content\*.md" | ForEach-Object {
    Write-Host "  • $($_.BaseName)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🔧 To upload to GitHub Wiki:" -ForegroundColor Green
Write-Host "  1. Go to: https://github.com/RicherTunes/Brainarr/wiki" -ForegroundColor White
Write-Host "  2. Click 'Create the first page'" -ForegroundColor White
Write-Host "  3. Copy content from wiki-content\Home.md" -ForegroundColor White
Write-Host "  4. Save as 'Home'" -ForegroundColor White
Write-Host "  5. Repeat for other pages" -ForegroundColor White

Write-Host ""
Write-Host "💡 Alternative: Clone wiki repo after creating first page:" -ForegroundColor Green
Write-Host "  git clone https://github.com/RicherTunes/Brainarr.wiki.git" -ForegroundColor White
Write-Host "  cd Brainarr.wiki" -ForegroundColor White
Write-Host "  cp ../wiki-content/*.md ." -ForegroundColor White
Write-Host "  git add . && git commit -m 'Add comprehensive wiki documentation'" -ForegroundColor White
Write-Host "  git push" -ForegroundColor White

Write-Host ""
Write-Host "✅ Wiki setup complete! Happy documenting! 📚" -ForegroundColor Cyan