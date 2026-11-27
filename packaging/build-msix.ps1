param(
  [Parameter(Mandatory)][string]$Version,            # 4자리, 예: 1.0.0.0
  [Parameter(Mandatory)][string]$PublishDir,         # 앱 + imgconv.exe + 셸 확장 DLL이 모인 폴더
  [string]$AppExe = "ImageConverter.exe",
  [string]$IdentityName = "ImageConverter.QuickConverter",
  [string]$Publisher = "",                           # 비우면 PFX의 Subject를 사용
  [string]$PublisherDisplay = "ImageConverter",
  [string]$ShellClsid = "233119CD-5628-4066-AB18-D528BC05C51F",
  [string]$ShellDll = "ImageConverter.ShellExtension.dll",
  [string]$PfxPath = "",
  [string]$PfxPassword = "",
  [string]$OutDir = "$PSScriptRoot\out",
  # 설치 마법사용: 앱 파일 없이 매니페스트+아이콘만 담은 외부 위치(sparse) 패키지를 만듭니다.
  # 설치 폴더를 -ExternalLocation으로 등록하면 Win11 첫 화면 우클릭 메뉴가 나옵니다.
  [switch]$ContextMenuOnly
)
if ($ContextMenuOnly -and $IdentityName -eq "ImageConverter.QuickConverter") { $IdentityName = "ImageConverter.QuickConverter.ContextMenu" }
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Version must have 4 parts (e.g. 1.0.0.0): $Version" }
foreach ($f in "StoreLogo.png","Square44x44Logo.png","Square150x150Logo.png") {
  if (-not (Test-Path "$PSScriptRoot\Assets\$f")) { throw "Missing asset: packaging\Assets\$f" }
}
foreach ($f in $AppExe,$ShellDll) {
  if (-not (Test-Path (Join-Path $PublishDir $f))) { throw "Missing in PublishDir: $f" }
}

if ($PfxPath) {
  $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
  $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword, $flags)
  if (-not $Publisher) { $Publisher = $cert.Subject }
  elseif ($Publisher -ne $cert.Subject) { throw "Publisher '$Publisher' does not match certificate subject '$($cert.Subject)'" }
}
if (-not $Publisher) { $Publisher = "CN=ImageConverter Dev" }

$layout = Join-Path $OutDir "layout"
Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $layout | Out-Null
if (-not $ContextMenuOnly) {
  Copy-Item "$PublishDir\*" $layout -Recurse
  # 전체 MSIX는 x64 패키지입니다. ARM64용 우클릭 메뉴 DLL은 설치 마법사만 씁니다.
  Remove-Item "$layout\*.arm64.dll" -ErrorAction SilentlyContinue
}
Copy-Item "$PSScriptRoot\Assets" "$layout\Assets" -Recurse

# 우클릭 메뉴 블록은 shell-extension 폴더의 조각 파일을 그대로 사용(확장자 목록 중복 방지)
$frag = Get-Content "$PSScriptRoot\shell-extension\AppxManifest.fragment.xml" -Raw -Encoding UTF8
$frag = [regex]::Replace($frag, '(?s)<!--.*?-->', '').Trim()
$frag = $frag.Replace("233119CD-5628-4066-AB18-D528BC05C51F", $ShellClsid).Replace("ImageConverter.ShellExtension.dll", $ShellDll)

$esc = { param($v) [Security.SecurityElement]::Escape($v) }
$template = if ($ContextMenuOnly) { "ContextMenuManifest.template.xml" } else { "AppxManifest.template.xml" }
$m = Get-Content "$PSScriptRoot\$template" -Raw -Encoding UTF8
$m = $m.Replace("__CONTEXT_MENU_EXTENSIONS__", $frag)
$map = @{ __IDENTITY_NAME__=$IdentityName; __PUBLISHER__=$Publisher; __PUBLISHER_DISPLAY__=$PublisherDisplay
          __VERSION__=$Version; __APP_EXE__=$AppExe; __SHELL_CLSID__=$ShellClsid; __SHELL_DLL__=$ShellDll }
foreach ($k in $map.Keys) { $m = $m.Replace($k, (& $esc $map[$k])) }
[xml]$null = $m   # 잘못된 XML이면 여기서 실패
Set-Content "$layout\AppxManifest.xml" $m -Encoding UTF8

$kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$makeappx = Get-ChildItem "$kits\10.*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
  Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
$signtool = Get-ChildItem "$kits\10.*\x64\signtool.exe" -ErrorAction SilentlyContinue |
  Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
if (-not $makeappx -or -not $signtool) { throw "Windows 10/11 SDK (makeappx/signtool x64) not found under $kits" }

$msix = Join-Path $OutDir $(if ($ContextMenuOnly) { "ImageConverter.ContextMenu.msix" } else { "ImageConverter.msix" })
Remove-Item $msix -ErrorAction SilentlyContinue
# /nv: 외부 위치 패키지는 매니페스트가 가리키는 exe·dll이 패키지 안에 없으므로 그 검사를 건너뜁니다.
$packArgs = @("pack", "/d", $layout, "/p", $msix, "/o")
if ($ContextMenuOnly) { $packArgs += "/nv" }
& $makeappx.FullName @packArgs
if ($LASTEXITCODE) { throw "makeappx failed" }
if ($PfxPath) {
  & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $PfxPassword /tr http://timestamp.digicert.com /td SHA256 $msix
  if ($LASTEXITCODE) { throw "signtool failed" }
  # 사용자가 신뢰 등록할 수 있게 공개 인증서도 함께 내보냄
  [IO.File]::WriteAllBytes((Join-Path $OutDir "ImageConverter.cer"), $cert.Export('Cert'))
}
Write-Host "Built $msix (publisher: $Publisher)"
