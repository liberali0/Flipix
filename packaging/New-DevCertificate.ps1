param(
  [string]$Subject = "CN=ImageConvert Dev",
  [string]$OutDir = "$PSScriptRoot\out",
  [string]$Password = ""
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
  -FriendlyName "ImageConvert signing" -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}") -NotAfter (Get-Date).AddYears(3)
Export-Certificate -Cert $cert -FilePath "$OutDir\ImageConverter.cer" | Out-Null
if ($Password) {
  $pw = ConvertTo-SecureString $Password -AsPlainText -Force
  Export-PfxCertificate -Cert $cert -FilePath "$OutDir\ImageConverter.pfx" -Password $pw | Out-Null
}
Write-Host "Subject(=Publisher): $($cert.Subject)`nThumbprint: $($cert.Thumbprint)"
