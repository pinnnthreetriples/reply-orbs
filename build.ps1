param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework compiler is not available.' }
$wpfRoot = Join-Path $frameworkRoot 'WPF'
$iconPath = Join-Path $projectRoot 'ReplyOrbs.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 64,64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $darkBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,25,25,25))
    $lightBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,255,255,233))
    $graphics.FillEllipse($darkBrush,0,0,64,64)
    $graphics.FillEllipse($lightBrush,9,24,16,16)
    $graphics.FillEllipse($lightBrush,25,24,16,16)
    $graphics.FillEllipse($lightBrush,41,24,16,16)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $stream.ToArray()
    $writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($iconPath))
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
    $writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$pngBytes.Length); $writer.Write([UInt32]22); $writer.Write($pngBytes)
    $writer.Dispose(); $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $darkBrush.Dispose(); $lightBrush.Dispose()
}
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Net.Http.dll','System.Security.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfRoot $_) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$exePath = Join-Path $projectRoot 'ReplyOrbs.exe'
& $compiler '/nologo' '/target:winexe' '/platform:x64' '/optimize+' '/warn:4' '/warnaserror+' '/codepage:65001' ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/out:' + $exePath) $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output ('Built: ' + $exePath)
if ($Test) {
    $testRoot = Join-Path $projectRoot 'work/test-runs'
    $runRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    $reportPath = Join-Path $projectRoot 'verification.txt'
    $testProcess = Start-Process -FilePath $exePath -ArgumentList @('--self-test','--report',('"' + $reportPath + '"'),'--test-root',('"' + $runRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($testProcess.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Encoding UTF8 | Write-Output }
        throw ('Verification failed: ' + $reportPath)
    }
    Get-Content -LiteralPath $reportPath -Encoding UTF8
}
