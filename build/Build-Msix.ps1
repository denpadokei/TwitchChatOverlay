#Requires -Version 7.0
<#
.SYNOPSIS
    TwitchChatOverlay の MSIX パッケージをビルド（必要なら自己署名）します。

.DESCRIPTION
    1. dotnet publish で自己完結 (self-contained) のレイアウトを作成
    2. AppxManifest.xml とタイル画像をレイアウトへ配置
    3. MakeAppx.exe で .msix を生成
    4. signtool.exe で署名（証明書未指定時は自己署名証明書を自動生成）
    5. SHA256 チェックサムを出力

    Windows SDK（MakeAppx / signtool）が必要です。
    Visual Studio は不要です。

.PARAMETER Version
    MSIX のバージョン (例: 0.4.1 / 0.4.1.0)。省略時は csproj の <Version> を使用します。

.PARAMETER CertificatePath
    リリース署名用の PFX ファイル。省略時は自己署名証明書を使用します。

.PARAMETER CertificatePassword
    PFX のパスワード（SecureString）。

.PARAMETER CertificateThumbprint
    証明書ストア (CurrentUser\My) の拇印を指定して署名します。

.PARAMETER SelfSign
    証明書未指定時に自己署名証明書を作成/再利用します（既定で有効）。

.EXAMPLE
    pwsh -File build/Build-Msix.ps1

.EXAMPLE
    pwsh -File build/Build-Msix.ps1 -Version 0.5.0 -CertificatePath ./mycert.pfx -CertificatePassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Version,

    [string]$OutputDir,

    [string]$IdentityName = 'denpadokei.TwitchChatOverlay',
    [string]$DisplayName = 'Twitch Chat Overlay',
    [string]$Description = 'Twitch / YouTube のチャットとチャンネルイベントをデスクトップに通知するオーバーレイアプリ',
    [string]$PublisherDisplayName = 'denpadokei',

    # 証明書未指定時に使う Publisher（署名証明書の Subject と完全一致させる必要がある）
    [string]$Publisher = 'CN=denpadokei',

    [string]$CertificatePath,
    [securestring]$CertificatePassword,
    [string]$CertificateThumbprint,

    [switch]$NoSign,
    [switch]$SkipChecksum,
    [switch]$KeepLayout,
    [switch]$SkipPublish,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$WindowsSdkBin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-SdkTool {
    param([Parameter(Mandatory)][string]$Name)

    if ($WindowsSdkBin) {
        $candidate = Join-Path $WindowsSdkBin $Name
        if (Test-Path -LiteralPath $candidate) { return $candidate }
        throw "指定された WindowsSdkBin に $Name が見つかりません: $WindowsSdkBin"
    }

    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    )

    $candidates = foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Directory |
            Where-Object Name -match '^\d+\.\d+\.\d+\.\d+$' |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object {
                $archDir = Join-Path $_.FullName 'x64'
                $tool = Join-Path $archDir $Name
                if (Test-Path -LiteralPath $tool) { $tool }
            }
    }

    $tool = $candidates | Select-Object -First 1
    if (-not $tool) {
        $command = Get-Command $Name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
        throw "$Name が見つかりません。Windows SDK をインストールするか -WindowsSdkBin を指定してください。"
    }

    return $tool
}

function ConvertTo-MsixVersion {
    param([Parameter(Mandatory)][string]$Value)

    $normalized = $Value.Trim().TrimStart('v', 'V')
    $parts = @($normalized.Split('.'))
    while ($parts.Count -lt 4) { $parts += '0' }

    if ($parts.Count -ne 4) {
        throw "MSIX のバージョンは 4 桁以下で指定してください: $Value"
    }

    foreach ($part in $parts) {
        $number = 0
        if (-not [int]::TryParse($part, [ref]$number) -or $number -lt 0 -or $number -gt 65535) {
            throw "MSIX のバージョン要素が不正です: $Value"
        }
    }

    return ($parts -join '.')
}

# ---------------------------------------------------------------
# 1. バージョン / 出力先の解決
# ---------------------------------------------------------------
$csprojPath = Join-Path $repoRoot 'TwitchChatOverlay\TwitchChatOverlay.csproj'
if (-not $Version) {
    [xml]$csproj = Get-Content -LiteralPath $csprojPath -Raw
    $Version = @(
        $csproj.Project.PropertyGroup | ForEach-Object {
            if ($_.PSObject.Properties['Version']) { $_.Version }
        } | Where-Object { $_ }
    ) | Select-Object -First 1
    if (-not $Version) { throw 'csproj から <Version> を取得できませんでした。-Version を指定してください。' }
}

$msixVersion = ConvertTo-MsixVersion -Value $Version
# ファイル名は 4 桁目が 0 のときだけ 3 桁に縮める（1.2.3.4 と 1.2.3.0 の上書き衝突を避ける）
$fileVersion = if ($msixVersion.EndsWith('.0')) { $msixVersion.Substring(0, $msixVersion.Length - 2) } else { $msixVersion }
$arch = switch ($Runtime) {
    'win-x64' { 'x64' }
    'win-arm64' { 'arm64' }
    default { throw "未対応の Runtime です: $Runtime" }
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot 'artifacts\msix'
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).ProviderPath

$layoutDir = Join-Path $OutputDir 'layout'
$packageBaseName = "TwitchChatOverlay-$fileVersion-$arch"
$msixPath = Join-Path $OutputDir "$packageBaseName.msix"

Write-Host "リポジトリ : $repoRoot"
Write-Host "バージョン : $msixVersion ($Configuration / $Runtime)"
Write-Host "出力先     : $OutputDir"

# ---------------------------------------------------------------
# 2. レイアウト作成（publish 出力そのものをパッケージの中身にする）
# ---------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Step 'dotnet publish (self-contained)'
    if (Test-Path -LiteralPath $layoutDir) {
        Remove-Item -LiteralPath $layoutDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null

    $publishArgs = @(
        'publish', $csprojPath,
        '--configuration', $Configuration,
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '--output', $layoutDir,
        "-p:Version=$msixVersion",
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false'
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish が失敗しました (exit code $LASTEXITCODE)"
    }

    # デバッグシンボルはパッケージに含めない
    # （-p:DebugType=None を渡すと WPF の MarkupCompile 用一時プロジェクトが
    #   protobuf 生成コードを取り込めずコンパイルに失敗するため、後から削除する）
    Get-ChildItem -LiteralPath $layoutDir -Filter '*.pdb' -File -Recurse |
        Remove-Item -Force -ErrorAction SilentlyContinue
}
elseif (-not (Test-Path -LiteralPath (Join-Path $layoutDir 'TwitchChatOverlay.exe'))) {
    throw "-SkipPublish を指定しましたが、レイアウトが存在しません: $layoutDir"
}

# パッケージに必要なファイルが揃っているか確認する
$requiredLayoutFiles = @(
    'TwitchChatOverlay.exe',
    'TwitchChatOverlay.dll',
    'Docs\PrivacyPolicy.html',
    'Docs\TermsOfUse.html'
)
foreach ($file in $requiredLayoutFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $layoutDir $file))) {
        throw "レイアウトに必須ファイルがありません: $file"
    }
}

# ---------------------------------------------------------------
# 3. 署名証明書の解決
# ---------------------------------------------------------------
$cert = $null
$cerPath = $null
$timestampUsed = $null
# PFX を一時的にストアへインポートした場合の後始末用
$tempStoreThumbprint = $null

if (-not $NoSign) {
    Write-Step '署名証明書の準備'

    # パスワードはプロセス引数に残さないよう、環境変数からの読み取りにも対応する
    if (-not $CertificatePassword -and $env:MSIX_CERTIFICATE_PASSWORD) {
        $CertificatePassword = ConvertTo-SecureString -String $env:MSIX_CERTIFICATE_PASSWORD -AsPlainText -Force
    }

    if ($CertificatePath) {
        if (-not (Test-Path -LiteralPath $CertificatePath)) {
            throw "証明書ファイルが見つかりません: $CertificatePath"
        }

        $pfxPath = (Resolve-Path -LiteralPath $CertificatePath).ProviderPath
        $ephemeralFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet

        # 事前に拇印だけ確認する（既存の証明書を誤って削除しないため）
        if ($CertificatePassword) {
            $probe = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $pfxPath,
                [System.Net.NetworkCredential]::new('', $CertificatePassword).Password,
                $ephemeralFlags)
        }
        else {
            $probe = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath)
        }
        $thumbprint = $probe.Thumbprint
        $preExisting = Test-Path -LiteralPath "Cert:\CurrentUser\My\$thumbprint"
        $probe.Dispose()

        # 署名はストアの証明書（/sha1）で行い、パスワードを signtool の引数に載せない
        $importParams = @{
            FilePath          = $pfxPath
            CertStoreLocation = 'Cert:\CurrentUser\My'
        }
        if ($CertificatePassword) {
            $importParams.Password = $CertificatePassword
        }
        $cert = Import-PfxCertificate @importParams
        Write-Host "PFX を一時的にストアへインポートして署名します: $($cert.Subject)"

        if (-not $preExisting) {
            $tempStoreThumbprint = $cert.Thumbprint
        }
    }
    elseif ($CertificateThumbprint) {
        $thumb = $CertificateThumbprint -replace '\s', ''
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $thumb | Select-Object -First 1
        if (-not $cert) {
            throw "CurrentUser\My に拇印 $thumb の証明書が見つかりません。"
        }
        Write-Host "証明書ストアを使用: $($cert.Subject)"
    }
    else {
        $subject = $Publisher
        $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object {
                $_.Subject -eq $subject -and
                $_.HasPrivateKey -and
                $_.NotAfter -gt (Get-Date).AddDays(30)
            } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

        if (-not $cert) {
            Write-Host "自己署名証明書を作成します: $subject"
            $cert = New-SelfSignedCertificate `
                -Type CodeSigningCert `
                -Subject $subject `
                -FriendlyName 'TwitchChatOverlay MSIX (self-signed)' `
                -CertStoreLocation 'Cert:\CurrentUser\My' `
                -KeyUsage DigitalSignature `
                -NotAfter (Get-Date).AddYears(3)
        }
        else {
            Write-Host "既存の自己署名証明書を再利用します: $($cert.Thumbprint)"
        }
    }

    # Identity の Publisher は署名証明書の Subject と一致している必要がある
    if ($Publisher -and $cert.Subject -ne $Publisher) {
        Write-Verbose "Publisher を証明書の Subject に合わせます: $Publisher -> $($cert.Subject)"
    }
    $Publisher = $cert.Subject

    # 証明書の公開鍵は常に出力する（自己署名の配布や証明書更新時の信頼に使用）
    $cerPath = Join-Path $OutputDir "$packageBaseName.cer"
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Write-Host "公開証明書: $cerPath"
}

Write-Host "Publisher  : $Publisher"

# ---------------------------------------------------------------
# 4. マニフェストとアセットの配置
# ---------------------------------------------------------------
Write-Step 'AppxManifest.xml / タイル画像の配置'

$manifestTemplate = Join-Path $repoRoot 'packaging\AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifestTemplate)) {
    throw "マニフェストテンプレートが見つかりません: $manifestTemplate"
}

$assetsSource = Join-Path $repoRoot 'packaging\Assets'
$requiredAssets = @(
    'Square44x44Logo.png',
    'Square150x150Logo.png',
    'Square310x310Logo.png',
    'StoreLogo.png',
    'Wide310x150Logo.png'
)
foreach ($asset in $requiredAssets) {
    if (-not (Test-Path -LiteralPath (Join-Path $assetsSource $asset))) {
        throw "タイル画像が見つかりません: packaging/Assets/$asset （build/New-MsixAssets.ps1 で生成できます）"
    }
}

$layoutAssets = Join-Path $layoutDir 'Assets'
New-Item -ItemType Directory -Path $layoutAssets -Force | Out-Null
Copy-Item -Path (Join-Path $assetsSource '*') -Destination $layoutAssets -Force

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$replacements = [ordered]@{
    '{{IDENTITY_NAME}}'          = $IdentityName
    '{{PUBLISHER}}'              = $Publisher
    '{{VERSION}}'                = $msixVersion
    '{{ARCH}}'                   = $arch
    '{{DISPLAY_NAME}}'           = $DisplayName
    '{{DESCRIPTION}}'            = $Description
    '{{PUBLISHER_DISPLAY_NAME}}' = $PublisherDisplayName
}
foreach ($key in $replacements.Keys) {
    $manifest = $manifest.Replace($key, [System.Security.SecurityElement]::Escape($replacements[$key]))
}

if ($manifest -match '\{\{[^}]+\}\}') {
    throw "AppxManifest.xml に未置換のトークンが残っています: $($Matches[0])"
}

$manifestPath = Join-Path $layoutDir 'AppxManifest.xml'
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding utf8NoBOM

# ---------------------------------------------------------------
# 5. MakeAppx でパッケージ化
# ---------------------------------------------------------------
Write-Step 'MakeAppx.exe でパッケージ化'

$makeAppx = Get-SdkTool -Name 'makeappx.exe'
Write-Host "makeappx: $makeAppx"

if (Test-Path -LiteralPath $msixPath) {
    Remove-Item -LiteralPath $msixPath -Force
}

& $makeAppx pack /d $layoutDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx pack が失敗しました (exit code $LASTEXITCODE)"
}
if (-not (Test-Path -LiteralPath $msixPath)) {
    throw "MSIX が生成されませんでした: $msixPath"
}

# ---------------------------------------------------------------
# 6. 署名
# ---------------------------------------------------------------
if (-not $NoSign) {
    Write-Step 'signtool.exe で署名'

    $signtool = Get-SdkTool -Name 'signtool.exe'
    Write-Host "signtool : $signtool"

    # 署名はストアの証明書（/sha1）で行う。PFX のパスワードを子プロセスの引数に載せない。
    $signArgs = @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint)
    $requireTimestamp = [bool]$CertificatePath

    try {
        $signed = $false
        if ($TimestampUrl) {
            & $signtool @signArgs /tr $TimestampUrl /td SHA256 $msixPath
            if ($LASTEXITCODE -eq 0) {
                $signed = $true
                $timestampUsed = $TimestampUrl
            }
            elseif ($requireTimestamp) {
                throw "タイムスタンプ付き署名に失敗しました (exit code $LASTEXITCODE)。リリース用の署名ではタイムスタンプを必須にしています。"
            }
            else {
                Write-Warning 'タイムスタンプ付き署名に失敗しました。タイムスタンプなしで再試行します。'
            }
        }

        if (-not $signed) {
            if ($requireTimestamp) {
                throw 'リリース用の署名ではタイムスタンプが必須です。-TimestampUrl を指定してください。'
            }

            & $signtool @signArgs $msixPath
            if ($LASTEXITCODE -ne 0) {
                throw "signtool sign が失敗しました (exit code $LASTEXITCODE)"
            }
        }

        # 署名の検証（自己署名証明書が未信頼の場合は警告のみ）
        & $signtool verify /pa $msixPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Warning '署名の検証に失敗しました（証明書チェーンが信頼されていない場合は想定どおりです）。'
            if ($cerPath) {
                Write-Warning "次のコマンドで信頼させてからインストールしてください: Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
            }
        }
    }
    catch {
        # 署名に失敗した未署名パッケージを成果物として残さない
        Remove-Item -LiteralPath $msixPath -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        if ($tempStoreThumbprint) {
            Remove-Item -LiteralPath "Cert:\CurrentUser\My\$tempStoreThumbprint" -Force -ErrorAction SilentlyContinue
        }
    }
}

# ---------------------------------------------------------------
# 7. チェックサム
# ---------------------------------------------------------------
$hash = $null
$checksumPath = $null
if ($SkipChecksum) {
    Write-Step 'SHA256 チェックサムの出力（スキップ）'
    Write-Host '署名後に別途生成してください（SignPath など外部で署名する場合）。'
}
else {
    Write-Step 'SHA256 チェックサムの出力'

    $hash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = "$msixPath.sha256"
    # UpdateService は "<hex>  <filename>" 形式（sha256sum 標準）を期待する
    Set-Content -LiteralPath $checksumPath -Value "$hash  $([System.IO.Path]::GetFileName($msixPath))" -Encoding utf8NoBOM
}

if (-not $KeepLayout) {
    Remove-Item -LiteralPath $layoutDir -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------
# 8. 結果
# ---------------------------------------------------------------
Write-Step '完了'
Write-Host ("パッケージ   : {0}" -f $msixPath)
Write-Host ("サイズ       : {0:N1} MB" -f ((Get-Item -LiteralPath $msixPath).Length / 1MB))
if ($checksumPath) {
    Write-Host ("SHA256       : {0}" -f $hash)
    Write-Host ("チェックサム : {0}" -f $checksumPath)
}
if ($cerPath) {
    Write-Host ("公開証明書   : {0}" -f $cerPath)
}
Write-Host ("署名         : {0}" -f ($(if ($NoSign) { 'なし' } else { $cert.Subject })))
if ($timestampUsed) {
    Write-Host ("タイムスタンプ: {0}" -f $timestampUsed)
}
