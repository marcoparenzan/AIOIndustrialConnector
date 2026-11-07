<#
.SYNOPSIS
    Builds and publishes one protocol mode (Modbus or S7) of the industrial connector to Azure Container Registry
    and registers it as a connector template on an Azure IoT Operations instance.

.DESCRIPTION
    1. Builds the connector container image with the .NET SDK (no Docker needed) and pushes it to ACR. The image is
       aio-<protocol>-connector: the same executable for every protocol, with CONNECTOR_PROTOCOL baked in.
    2. Optionally builds and pushes the protocol's simulator image too (-IncludeSimulator).
    3. Renders deploy/<protocol>/connector-metadata.json with the registry name and pushes it to ACR with ORAS,
       using the Akri connector metadata media type.
    4. Creates the connector template with `az iot ops connector template create` (skip with -SkipTemplate).

    Requirements: .NET SDK 10+, Azure CLI with the azure-iot-ops extension, ORAS CLI, `az login` done.
    The ACR must be the registry configured for the AIO instance (anonymous pull, or a pull secret
    configured on the connector template).

.EXAMPLE
    ./deploy/Publish-Connector.ps1 -Protocol modbus -AcrName myacr -ResourceGroup rg-aio -Instance aio-instance -IncludeSimulator
    ./deploy/Publish-Connector.ps1 -Protocol s7     -AcrName myacr -ResourceGroup rg-aio -Instance aio-instance -IncludeSimulator
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateSet('modbus', 's7')] [string] $Protocol,
    [Parameter(Mandatory)] [string] $AcrName,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $Instance,
    [string] $TemplateName = $(if ($Protocol -eq 's7') { 's7-connector' } else { 'modbus-tcp-connector' }),
    [string] $Version = '0.2.0',
    [switch] $IncludeSimulator,
    [switch] $SkipTemplate
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$registry = "$AcrName.azurecr.io"
$image = "aio-$Protocol-connector"
$simulatorProject = if ($Protocol -eq 's7') { 'src/S7/S7Simulator/S7Simulator.csproj' } else { 'src/Modbus/ModbusSimulator/ModbusSimulator.csproj' }
$simulatorImage = "aio-$Protocol-simulator"
# Scratch folder: $env:BUILD_ARTIFACTS, else D:\Dev\BuildArtifacts\AIOIndustrialConnector when present, else <repo>\artifacts (git-ignored).
$artifacts = if ($env:BUILD_ARTIFACTS) { $env:BUILD_ARTIFACTS } elseif (Test-Path 'D:\Dev\BuildArtifacts') { 'D:\Dev\BuildArtifacts\AIOIndustrialConnector' } else { Join-Path $repo 'artifacts' }
$work = Join-Path $artifacts "tmp\publish-$Version"
New-Item -ItemType Directory -Force $work | Out-Null

function Invoke-Checked([string] $what, [scriptblock] $command) {
    Write-Host "==> $what" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

# --- Registry credentials for the .NET container tooling and ORAS (token valid ~3 hours) ---
Write-Host "==> Getting an ACR access token for $registry" -ForegroundColor Cyan
$token = az acr login --name $AcrName --expose-token --query accessToken -o tsv
if (-not $token) { throw "Could not get an ACR token. Run 'az login' and check access to $AcrName." }
$aadUser = '00000000-0000-0000-0000-000000000000'
$env:DOTNET_CONTAINER_REGISTRY_UNAME = $aadUser
$env:DOTNET_CONTAINER_REGISTRY_PWORD = $token

# --- Images ---
if ($PSCmdlet.ShouldProcess("$registry/${image}:$Version", 'build and push image')) {
    Invoke-Checked "Build and push connector image ($Protocol mode)" {
        dotnet publish (Join-Path $repo 'src/Host/IndustrialConnector/IndustrialConnector.csproj') -c Release -r linux-x64 `
            -t:PublishContainer -p:ConnectorProtocol=$Protocol -p:ContainerRegistry=$registry -p:ContainerImageTag=$Version
    }
}

if ($IncludeSimulator -and $PSCmdlet.ShouldProcess("$registry/${simulatorImage}:$Version", 'build and push image')) {
    Invoke-Checked 'Build and push simulator image' {
        dotnet publish (Join-Path $repo $simulatorProject) -c Release -r linux-x64 `
            -t:PublishContainer -p:ContainerRegistry=$registry -p:ContainerImageTag=$Version
    }
}

# --- Connector metadata (OCI artifact) ---
$metadata = Join-Path $work 'connector-metadata.json'
(Get-Content -Raw (Join-Path $PSScriptRoot "$Protocol/connector-metadata.json")) `
    -replace '__REGISTRY__', $registry `
    -replace '"tag": "[^"]+"', "`"tag`": `"$Version`"" |
    Set-Content -NoNewline -Encoding utf8NoBOM $metadata
$emptyConfig = Join-Path $work 'empty-config.json'
Set-Content -NoNewline -Encoding utf8NoBOM $emptyConfig '{}'

$metadataRef = "$registry/$image-metadata:$Version"
if ($PSCmdlet.ShouldProcess($metadataRef, 'push connector metadata')) {
    Invoke-Checked 'ORAS login' { $token | oras login $registry --username $aadUser --password-stdin }
    Push-Location $work
    try {
        # Relative file names: ORAS stores them as the artifact titles.
        Invoke-Checked "Push connector metadata to $metadataRef" {
            oras push --config "empty-config.json:application/vnd.microsoft.akri-connector.v1+json" `
                $metadataRef "connector-metadata.json:application/json"
        }
    }
    finally { Pop-Location }
}

# --- Connector template on the AIO instance ---
if (-not $SkipTemplate -and $PSCmdlet.ShouldProcess("$Instance/$TemplateName", 'create connector template')) {
    Invoke-Checked "Create connector template $TemplateName" {
        az iot ops connector template create --name $TemplateName --resource-group $ResourceGroup `
            --instance $Instance --connector-metadata-ref $metadataRef --replicas 1 --log-level info
    }
    az iot ops connector template list --resource-group $ResourceGroup --instance $Instance --output table
}

Write-Host ''
Write-Host "Done. Image: $registry/${image}:$Version  Metadata: $metadataRef" -ForegroundColor Green
Write-Host 'Next: create a device and asset (deploy/$Protocol/New-SampleAsset.ps1 or the operations experience).'
