# Create GitHub Wiki Pages via Web Interface
# Script to guide manual wiki page creation with prepared content

Write-Host "🧠 Brainarr Wiki Creation Guide" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan

$pages = @(
    @{Name="Home"; File="Home.md"; Order=1}
    @{Name="Installation"; File="Installation.md"; Order=2}
    @{Name="Provider Setup"; File="Provider-Setup.md"; Order=3}
    @{Name="Local Providers"; File="Local-Providers.md"; Order=4}  
    @{Name="Cloud Providers"; File="Cloud-Providers.md"; Order=5}
    @{Name="First Run Guide"; File="First-Run-Guide.md"; Order=6}
    @{Name="Advanced Settings"; File="Advanced-Settings.md"; Order=7}
    @{Name="Troubleshooting"; File="Troubleshooting.md"; Order=8}
)

Write-Host "📚 Pages to create (in order):" -ForegroundColor Green
foreach ($page in $pages | Sort-Object Order) {
    Write-Host "  $($page.Order). $($page.Name)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🔧 Manual Creation Process:" -ForegroundColor Green
Write-Host "  1. Go to: https://github.com/RicherTunes/Brainarr/wiki" -ForegroundColor White
Write-Host "  2. If no pages exist, click 'Create the first page'" -ForegroundColor White
Write-Host "  3. If pages exist, click 'New Page' button" -ForegroundColor White
Write-Host "  4. Follow prompts below for each page..." -ForegroundColor White

Write-Host ""
Write-Host "📄 Page Creation Steps:" -ForegroundColor Green

foreach ($page in $pages | Sort-Object Order) {
    Write-Host ""
    Write-Host "=== $($page.Name) ===" -ForegroundColor Cyan
    Write-Host "📝 Page Title: $($page.Name)" -ForegroundColor White
    Write-Host "📁 Content File: wiki-content\$($page.File)" -ForegroundColor White
    Write-Host "🔗 Wiki URL: https://github.com/RicherTunes/Brainarr/wiki/$($page.Name -replace ' ','-')" -ForegroundColor White
    
    if (Test-Path "wiki-content\$($page.File)") {
        Write-Host "✅ Content ready" -ForegroundColor Green
    } else {
        Write-Host "❌ Content file missing" -ForegroundColor Red
    }
    
    Write-Host "Press Enter to continue to next page..." -ForegroundColor Gray
    Read-Host
}

Write-Host ""
Write-Host "🎉 All pages created!" -ForegroundColor Green
Write-Host "🔗 Visit your complete wiki: https://github.com/RicherTunes/Brainarr/wiki" -ForegroundColor Cyan

Write-Host ""
Write-Host "💡 Pro Tips:" -ForegroundColor Yellow
Write-Host "  • Use 'Edit' button to refine pages after creation" -ForegroundColor White
Write-Host "  • Wiki pages support full Markdown formatting" -ForegroundColor White  
Write-Host "  • Link between pages using [[Page Name]] syntax" -ForegroundColor White
Write-Host "  • Add images by uploading to wiki and linking" -ForegroundColor White