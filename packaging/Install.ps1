# 사용법: powershell -ExecutionPolicy Bypass -File Install.ps1
# 인증서 신뢰 등록만 관리자 권한으로 하고, 패키지는 현재 사용자 계정으로 설치합니다.
#
# 보안 확인:
#   1) SHA256SUMS.txt가 같은 폴더에 있으면 MSIX·인증서 파일의 해시가 맞는지 먼저 확인합니다.
#   2) MSIX가 바로 그 인증서로 서명됐는지 확인한 뒤에만 인증서를 신뢰 목록에 넣습니다.
#   3) 관리자 권한 명령에는 인증서 내용이 그대로 들어가므로, 확인 뒤에 파일을 바꿔치기해도 소용없습니다.
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$cer = Join-Path $dir "ImageConverter.cer"
$msix = Join-Path $dir "ImageConverter.msix"
$sums = Join-Path $dir "SHA256SUMS.txt"

if (Test-Path $sums) {
  foreach ($line in Get-Content $sums) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { continue }
    $name = $Matches[2].Trim()
    if ($name -notin 'ImageConverter.msix', 'ImageConverter.cer') { continue }
    $actual = (Get-FileHash -Algorithm SHA256 (Join-Path $dir $name)).Hash
    if ($actual -ne $Matches[1].ToUpperInvariant()) { throw "$name 파일이 릴리스 원본과 다릅니다(해시 불일치). 설치를 중단합니다." }
  }
}

if (Test-Path $cer) {
  $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2(,[IO.File]::ReadAllBytes($cer))
  $sig = Get-AuthenticodeSignature -FilePath $msix
  if (-not $sig.SignerCertificate -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
    throw "ImageConverter.msix가 함께 받은 인증서로 서명되지 않았습니다. 설치를 중단합니다."
  }
  $already = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint
  if (-not $already) {
    $b64 = [Convert]::ToBase64String($cert.RawData)
    $trust = @"
`$ErrorActionPreference = 'Stop'
`$c = New-Object Security.Cryptography.X509Certificates.X509Certificate2(,[Convert]::FromBase64String('$b64'))
if (`$c.Thumbprint -ne '$($cert.Thumbprint)') { exit 3 }
`$s = New-Object Security.Cryptography.X509Certificates.X509Store('TrustedPeople', 'LocalMachine')
`$s.Open('ReadWrite'); try { `$s.Add(`$c) } finally { `$s.Close() }
exit 0
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($trust))
    $p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded
    if ($p.ExitCode) { throw "인증서 등록에 실패했습니다." }
  }
}
Add-AppxPackage -Path $msix
Write-Host "설치 완료. 탐색기를 다시 시작하거나 로그아웃 후 우클릭 메뉴를 확인하세요."
