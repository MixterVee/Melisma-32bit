# Run once on your Windows PC. Keep the generated keystore private.
$ErrorActionPreference = 'Stop'
$keytool = (Get-Command keytool -ErrorAction SilentlyContinue).Source
if (-not $keytool) {
    $candidate = 'C:\Program Files\Android\Android Studio\jbr\bin\keytool.exe'
    if (Test-Path $candidate) { $keytool = $candidate }
}
if (-not $keytool) { throw 'keytool not found; install a JDK or use the one bundled with Android Studio.' }

$keystore = Join-Path (Get-Location) 'melisma-tv-release.jks'
if (Test-Path $keystore) { throw "Keystore already exists: $keystore - do not overwrite it." }

Write-Host 'Choose a strong signing password and keep it safe.' -ForegroundColor Yellow
& $keytool -genkeypair -v -keystore $keystore -alias melisma_tv -keyalg RSA -keysize 3072 -validity 10000 -storetype PKCS12 -dname 'CN=Personal Melisma TV Build'
if ($LASTEXITCODE -ne 0) { throw 'Key generation failed.' }

Write-Host "Private keystore created: $keystore" -ForegroundColor Green
Write-Host 'Back it up OFFLINE and never commit it or its passwords to GitHub.' -ForegroundColor Yellow
Write-Host 'GitHub -> Settings -> Secrets and variables -> Actions -> New repository secret:'
Write-Host '  KEYSTORE_BASE64  = base64-encoded entire .jks file (command below)'
Write-Host '  KEYSTORE_PASSWORD = the password you entered'
Write-Host '  KEY_ALIAS = melisma_tv'
Write-Host '  KEY_PASSWORD = the key password (usually the same with PKCS12)'
Write-Host "Run this to generate KEYSTORE_BASE64 privately (do not paste it into public issues):"
Write-Host "[Convert]::ToBase64String([IO.File]::ReadAllBytes('$keystore'))"
