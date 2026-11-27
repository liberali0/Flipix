param(
  [Parameter(Mandatory)][string]$Version,            # 예: 1.0.0
  [Parameter(Mandatory)][string]$PublishDir,         # 앱 + imgconv.exe + 셸 확장 DLL(x64, arm64)이 모인 폴더
  [string]$PfxPath = "",
  [string]$PfxPassword = "",
  [string]$OutDir = "$PSScriptRoot\out"
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be MAJOR.MINOR.PATCH: $Version" }
foreach ($f in "Flipix.exe","imgconv.exe","Flipix.ShellExtension.dll","Flipix.ShellExtension.arm64.dll") {
  if (-not (Test-Path (Join-Path $PublishDir $f))) { throw "Missing in PublishDir: $f" }
}
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe","$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (choco install innosetup)" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$isccArgs = @("/DAppVersion=$Version", "/DSourceDir=$((Resolve-Path $PublishDir).Path)", "/DOutDir=$((Resolve-Path $OutDir).Path)")

# Win11 우클릭 메뉴 패키지의 인증서를 신뢰 등록하는 관리자 명령을 미리 만들어 설치 파일 안에 넣습니다.
# 인증서 내용이 명령 안에 들어가므로, 관리자 권한으로 실행될 때 사용자가 쓸 수 있는 설치 폴더의 파일을
# 전혀 읽지 않습니다(바꿔치기로 다른 인증서를 신뢰시키거나 다른 스크립트를 관리자 권한으로 돌릴 수 없음).
$cerPath = Join-Path $PublishDir "Flipix.cer"
if (Test-Path $cerPath) {
  $cer = New-Object Security.Cryptography.X509Certificates.X509Certificate2(,[IO.File]::ReadAllBytes((Resolve-Path $cerPath).Path))
  $b64 = [Convert]::ToBase64String($cer.RawData)
  $trust = @"
`$ErrorActionPreference = 'Stop'
`$c = New-Object Security.Cryptography.X509Certificates.X509Certificate2(,[Convert]::FromBase64String('$b64'))
if (`$c.Thumbprint -ne '$($cer.Thumbprint)') { exit 3 }
`$s = New-Object Security.Cryptography.X509Certificates.X509Store('TrustedPeople', 'LocalMachine')
`$s.Open('ReadWrite'); try { `$s.Add(`$c) } finally { `$s.Close() }
exit 0
"@
  $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($trust))
  $isccArgs += "/DTrustCommand=$encoded"
  Write-Host "Context menu certificate pinned: $($cer.Subject) $($cer.Thumbprint)"
}
& $iscc @isccArgs "$PSScriptRoot\installer\ImageConverter.iss"
if ($LASTEXITCODE) { throw "ISCC failed" }
$setup = Join-Path $OutDir "Flipix-Setup-$Version.exe"
if ($PfxPath) {
  $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
  $signtool = Get-ChildItem "$kits\10.*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
  if (-not $signtool) { throw "signtool not found" }
  & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $PfxPassword /tr http://timestamp.digicert.com /td SHA256 $setup
  if ($LASTEXITCODE) { throw "signtool failed" }
}
Write-Host "Built $setup"
