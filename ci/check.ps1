$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    $trackedFiles = @(& git -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked files.' }
    $allowedPaths = '^(src/[^/]+\.cs|ci/[^/]+\.ps1|\.github/workflows/[^/]+\.yml|\.github/dependabot\.yml|\.github/main-protection\.json|\.github/pull_request_template\.md|\.gitignore|\.gitattributes|README\.md|build\.ps1|app\.manifest|ReplyOrbs\.exe\.config|ReplyOrbs\.ico)$'
    $credentialPatterns = @(
        'gh[pousr]_[A-Za-z0-9]{36,}',
        'github_pat_[A-Za-z0-9_]{40,}',
        '(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{32,}',
        '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    )
    foreach ($trackedFile in $trackedFiles) {
        if ($trackedFile -notmatch $allowedPaths) { throw ('Unexpected tracked path: ' + $trackedFile) }
        $filePath = Join-Path $repositoryRoot $trackedFile
        if ((Get-Item -LiteralPath $filePath).Length -gt 1048576) { throw ('File exceeds 1 MiB: ' + $trackedFile) }
        if ($trackedFile.EndsWith('.ico')) { continue }
        $fileText = [System.IO.File]::ReadAllText($filePath)
        foreach ($credentialPattern in $credentialPatterns) {
            if ($fileText -match $credentialPattern) { throw ('Possible plaintext credential in ' + $trackedFile + '. Credential value is not logged.') }
        }
        if ($trackedFile.EndsWith('.ps1')) {
            $parseTokens = $null; $parseErrors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($filePath, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
            if ($parseErrors.Count -ne 0) { throw ('PowerShell syntax error in ' + $trackedFile) }
        }
    }
    [xml]$manifest = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot 'app.manifest'))
    [xml]$runtime = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot 'ReplyOrbs.exe.config'))
    Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot '.github/main-protection.json') | ConvertFrom-Json | Out-Null
    & git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Staged changes contain whitespace errors.' }
    & git diff-tree --no-commit-id --check --root -m -r HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Commit contains whitespace errors.' }
    Write-Output ('PASS: repository paths, credential patterns, script syntax, XML/JSON and whitespace (' + $trackedFiles.Count + ' files).')
} finally { Pop-Location }
