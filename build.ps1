$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force -Path bin | Out-Null
    & $compiler /nologo /target:winexe /out:bin\AutoInput.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll Program.cs MainForm.cs KeyboardSender.cs Recording.cs RecordingStudio.cs ClipTimeline.cs HotkeySettings.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Output 'Built: bin\AutoInput.exe'
} finally { Pop-Location }
