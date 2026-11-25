# 빌드한 네이티브 DLL/EXE에 기본 보호 기능(ASLR, DEP, Control Flow Guard)이 켜져 있는지 확인합니다.
# 사용법: ./packaging/Test-PeHardening.ps1 -Path build/shell/Release/ImageConverter.ShellExtension.dll
param([Parameter(Mandatory)][string[]]$Path)
$ErrorActionPreference = 'Stop'
$required = [ordered]@{
  'HIGH_ENTROPY_VA (64-bit ASLR)' = 0x0020
  'DYNAMIC_BASE (ASLR)'           = 0x0040
  'NX_COMPAT (DEP)'               = 0x0100
  'GUARD_CF (Control Flow Guard)' = 0x4000
}
$failed = $false
foreach ($file in $Path) {
  $bytes = [IO.File]::ReadAllBytes((Resolve-Path $file).Path)
  if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "$file is not a PE file" }
  $pe = [BitConverter]::ToInt32($bytes, 0x3C)
  if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x00004550) { throw "$file has no PE header" }
  # DllCharacteristics: optional header(PE 헤더 + 24) 안 70바이트 위치(PE32, PE32+ 같음)
  $characteristics = [BitConverter]::ToUInt16($bytes, $pe + 24 + 70)
  foreach ($name in $required.Keys) {
    if (($characteristics -band $required[$name]) -eq 0) {
      Write-Host "::error::$file is missing $name"
      $failed = $true
    }
  }
  Write-Host ("{0}: DllCharacteristics=0x{1:X4}" -f $file, $characteristics)
}
if ($failed) { exit 1 }
