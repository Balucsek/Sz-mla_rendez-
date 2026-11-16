$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'InvoiceManager.csproj'
$installerScript = Join-Path $PSScriptRoot 'SzamlaRendezo.iss'
$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw 'Inno Setup 6 is required. Install it from https://jrsoftware.org/isdl.php and rerun this script.'
}

$publishDirectory = Join-Path $env:TEMP ('SzamlaRendezoPublish-' + [Guid]::NewGuid().ToString('N'))
$outputDirectory = Join-Path $PSScriptRoot 'output'

try {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

    & dotnet publish $projectFile -c Release -r win-x64 --self-contained true -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    & $compiler "/DPublishDir=$publishDirectory" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
}

Write-Output "Installer created: $(Join-Path $outputDirectory 'SzamlaRendezoSetup.exe')"
