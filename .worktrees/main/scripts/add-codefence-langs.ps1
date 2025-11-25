Param(
  [string]$Root = '.'
)

$ErrorActionPreference = 'Stop'

function Annotate-File {
  Param([string]$Path)
  $lines = Get-Content -Raw -Encoding UTF8 $Path -ErrorAction Stop
  $arr = $lines -split "`r?`n"
  $changed = $false
  $inside = $false
  for ($i = 0; $i -lt $arr.Length; $i++) {
    $line = $arr[$i]
    if ($line -match '^\s*```') {
      if (-not $inside) {
        # Opening fence
        if ($line -match '^\s*```\s*$') {
          $arr[$i] = '```text'
          $changed = $true
        }
        $inside = $true
      }
      else {
        # Closing fence
        $inside = $false
      }
    }
  }
  if ($changed) {
    ($arr -join "`n") | Set-Content -Encoding UTF8 $Path
    Write-Output "annotated: $Path"
  }
}

Get-ChildItem -Path $Root -Recurse -File -Include *.md |
  Where-Object { $_.FullName -notmatch '\\archive\\' } |
  ForEach-Object { Annotate-File -Path $_.FullName }

Write-Host "Completed code fence annotation."
