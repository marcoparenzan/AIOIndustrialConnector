<#
.SYNOPSIS
    Creates the sample Siemens S7 device and asset in Azure Device Registry with the Azure CLI.

.DESCRIPTION
    Cloud-side equivalent of deploy/s7/k8s/device.yaml + deploy/s7/k8s/asset.yaml: the resources are created
    through Azure Resource Manager, so they are visible and editable in the operations experience.

    The device endpoint points at -S7Address: the in-cluster simulator by default
    (deploy/s7/k8s/simulator.yaml), or a real PLC / gateway.

.EXAMPLE
    ./deploy/s7/New-SampleAsset.ps1 -ResourceGroup rg-aio -Instance aio-instance
    ./deploy/s7/New-SampleAsset.ps1 -ResourceGroup rg-aio -Instance aio-instance -S7Address s7://192.168.0.1:102
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $Instance,
    [string] $DeviceName = 's7-sim-device',
    [string] $EndpointName = 's7-sim-endpoint',
    [string] $AssetName = 's7-sim-asset',
    [string] $S7Address = 's7://s7-simulator.azure-iot-operations.svc.cluster.local:102',
    [string] $EndpointConfig = '{"cpuType": "S71500", "rack": 0, "slot": 1, "timeoutMs": 2000, "connectTimeoutMs": 3000}'
)

$ErrorActionPreference = 'Stop'
$common = @('--instance', $Instance, '--resource-group', $ResourceGroup)

function Invoke-Az([string] $what, [string[]] $azArgs) {
    if (-not $PSCmdlet.ShouldProcess($what)) { return }
    Write-Host "==> $what" -ForegroundColor Cyan
    az @azArgs --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

# Inline JSON passed to az must survive PowerShell -> native argument passing; write it to files instead.
# Scratch folder: $env:BUILD_ARTIFACTS, else D:\Dev\BuildArtifacts\AIOIndustrialConnector when present, else <repo>\artifacts (git-ignored).
$artifacts = if ($env:BUILD_ARTIFACTS) { $env:BUILD_ARTIFACTS } elseif (Test-Path 'D:\Dev\BuildArtifacts') { 'D:\Dev\BuildArtifacts\AIOIndustrialConnector' } else { Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'artifacts' }
$tmp = Join-Path $artifacts 'tmp\sample-asset'
New-Item -ItemType Directory -Force $tmp | Out-Null
function JsonFile([string] $name, [string] $json) {
    $path = Join-Path $tmp "$name.json"
    Set-Content -NoNewline -Encoding utf8NoBOM $path $json
    return $path
}

Invoke-Az "Create device $DeviceName" (@('iot', 'ops', 'ns', 'device', 'create', '--name', $DeviceName) + $common)

Invoke-Az "Add S7 endpoint $EndpointName -> $S7Address" (@(
    'iot', 'ops', 'ns', 'device', 'endpoint', 'inbound', 'add', 'custom',
    '--device', $DeviceName, '--name', $EndpointName,
    '--endpoint-type', 'Custom.SiemensS7', '--version', '1.0',
    '--endpoint-address', $S7Address,
    # The reference documents --additional-config as inline JSON only (no file path), so pass it inline.
    # PowerShell 7.3+ quotes native arguments correctly; on older shells escape the inner quotes.
    '--additional-config', $EndpointConfig) + $common)

Invoke-Az "Create asset $AssetName" (@(
    'iot', 'ops', 'ns', 'asset', 'custom', 'create', '--name', $AssetName,
    '--device', $DeviceName, '--endpoint', $EndpointName,
    '--dataset-config', (JsonFile 'asset-defaults' '{"samplingIntervalMs": 1000}')) + $common)

# Dataset 1: fast process values to MQTT
Invoke-Az 'Add dataset process' (@(
    'iot', 'ops', 'ns', 'asset', 'custom', 'dataset', 'add', '--asset', $AssetName, '--name', 'process',
    '--config', (JsonFile 'ds-process' '{"samplingIntervalMs": 1000}'),
    '--destination', "topic=s7/$DeviceName/$AssetName/process", 'qos=Qos1', 'retain=Never', 'ttl=3600') + $common)

# Dataset 2: slow identity values to the broker state store
Invoke-Az 'Add dataset identity' (@(
    'iot', 'ops', 'ns', 'asset', 'custom', 'dataset', 'add', '--asset', $AssetName, '--name', 'identity',
    '--config', (JsonFile 'ds-identity' '{"samplingIntervalMs": 30000}'),
    '--destination', "key=$AssetName-identity") + $common)

# Data points: dataset, name, S7 address, data point configuration (see docs/s7/CONFIGURATION.md)
$points = @(
    @('process', 'temperature', 'DB1.DBD0', '{"dataType": "Real"}'),
    @('process', 'humidity', 'DB1.DBW4', '{"dataType": "Int", "scale": 0.1}'),
    @('process', 'cycleCounter', 'DB1.DBD6', '{"dataType": "DWord"}'),
    @('process', 'pressureDelta', 'DB1.DBW10', '{"dataType": "Int"}'),
    @('process', 'running', 'DB1.DBX12.0', '{}'),
    @('process', 'alarm', 'DB1.DBX12.1', '{}'),
    @('process', 'tankLevelPercent', 'MW0', '{"scale": 0.024420024420024}'),
    @('process', 'doorClosed', 'I0.0', '{}'),
    @('process', 'emergencyStop', 'I0.1', '{}'),
    @('process', 'pumpOn', 'Q0.0', '{}'),
    @('process', 'valveOpen', 'Q0.1', '{}'),
    @('identity', 'serialNumber', 'DB1.DBB20', '{"dataType": "String", "length": 16}'),
    @('identity', 'totalEnergyKWh', 'DB1.DBB40', '{"dataType": "LReal"}')
)

foreach ($p in $points) {
    $dataset, $name, $source, $config = $p
    Invoke-Az "Add data point $dataset/$name ($source)" (@(
        'iot', 'ops', 'ns', 'asset', 'custom', 'datapoint', 'add', '--asset', $AssetName, '--dataset', $dataset,
        '--name', $name, '--data-source', $source, '--config', (JsonFile "dp-$name" $config), '--replace') + $common)
}

Write-Host ''
Write-Host "Done. Check: az iot ops ns asset custom dataset list --asset $AssetName -i $Instance -g $ResourceGroup -o table" -ForegroundColor Green
