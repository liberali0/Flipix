# 설치 마법사가 Windows 11 첫 화면 우클릭 메뉴를 켜고 끌 때 쓰는 도우미입니다(설치 폴더에 함께 설치됨).
#   Trust      : 패키지 서명 인증서를 이 PC에서 신뢰하도록 등록(관리자 권한 필요). 이미 있으면 아무것도 안 함.
#                개발·CI 확인용입니다. 설치 마법사는 이 스크립트를 관리자 권한으로 실행하지 않고,
#                인증서가 들어 있는 고정 명령(build-installer.ps1의 TrustCommand)을 씁니다.
#   IsTrusted  : 이미 신뢰하면 종료 코드 0, 아니면 1.
#   Register   : 설치 폴더를 외부 위치로 하는 메뉴 패키지를 현재 사용자에게 등록. 성공하면 0.
#   Unregister : 현재 사용자의 메뉴 패키지 제거.
param([Parameter(Mandatory)][ValidateSet('Trust', 'IsTrusted', 'Register', 'Unregister')][string]$Action)
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$packageName = 'ImageConverter.QuickConverter.ContextMenu'
$msix = Join-Path $dir 'Flipix.ContextMenu.msix'
$cer = Join-Path $dir 'Flipix.cer'

function Test-Trusted {
  if (-not (Test-Path $cer)) { return $false }
  $thumb = (New-Object Security.Cryptography.X509Certificates.X509Certificate2($cer)).Thumbprint
  return [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $thumb)
}

try {
  switch ($Action) {
    'IsTrusted' { if (Test-Trusted) { exit 0 } else { exit 1 } }
    'Trust' {
      if (-not (Test-Trusted)) { Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null }
      exit 0
    }
    'Register' {
      # 같은 버전이 이미 있으면 Add-AppxPackage가 실패하므로 먼저 지웁니다(업데이트·재설치 모두 같은 경로).
      Get-AppxPackage -Name $packageName | Remove-AppxPackage
      Add-AppxPackage -Path $msix -ExternalLocation $dir
      exit 0
    }
    'Unregister' {
      Get-AppxPackage -Name $packageName | Remove-AppxPackage
      exit 0
    }
  }
} catch {
  Write-Error $_
  exit 1
}
