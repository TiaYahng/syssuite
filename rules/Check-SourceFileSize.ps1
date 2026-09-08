[CmdletBinding()]
param(
    [ValidateRange(1, 10000)]
    [int]$MaxLines = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceExtensions = @('.cs', '.xaml', '.cpp', '.h', '.hpp', '.cc', '.cxx')
$sourceDirectories = @('src', 'tests', 'installer') | Where-Object { Test-Path $_ }
$excludedDirectories = @('\bin\', '\obj\', '\build\', '\out\', '\.vs\', '\.git\')
$violations = foreach ($directory in $sourceDirectories) {
    Get-ChildItem $directory -Recurse -File |
        Where-Object {
            $file = $_
            $sourceExtensions -contains $file.Extension.ToLowerInvariant() -and
            -not ($excludedDirectories | Where-Object { $file.FullName.IndexOf($_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 })
        } |
        ForEach-Object {
            $lineCount = 0
            foreach ($line in [System.IO.File]::ReadLines($_.FullName)) {
                $lineCount++
            }

            if ($lineCount -gt $MaxLines) {
                [pscustomobject]@{
                    Lines = $lineCount
                    MaxLines = $MaxLines
                    Path = [System.IO.Path]::GetRelativePath((Get-Location).Path, $_.FullName)
                }
            }
        }
}

if ($violations) {
    $violations | Sort-Object Lines -Descending | Format-Table -AutoSize
    throw "Source file size limit exceeded: $MaxLines lines."
}

Write-Host "Source file size check passed: $MaxLines lines max."
