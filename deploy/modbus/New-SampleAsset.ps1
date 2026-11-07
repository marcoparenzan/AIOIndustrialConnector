<#
.SYNOPSIS
    Creates the sample Modbus device and asset in Azure Device Registry with the Azure CLI.

.DESCRIPTION
    Cloud-side equivalent of deploy/modbus/k8s/device.yaml + deploy/modbus/k8s/asset.yaml: the resources are created
    through Azure Resource Manager, so they are visible and editable in the operations experience.

    The device endpoint points at -ModbusAddress: the in-cluster simulator by default
    (deploy/modbus/k8s/simulator.yaml), or a real PLC / gateway.

.EXAMPLE
    ./deploy/modbus/New-SampleAsset.ps1 -ResourceGroup rg-aio -Instance aio-instance
    ./deploy/modbus/New-SampleAsset.ps1 -ResourceGroup rg-aio -Instance aio-instance -ModbusAddress modbus-tcp://192.168.1.50:502
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $Instance,
    [string] $DeviceName = 'modbus-sim-device',
    [string] $EndpointName = 'modbus-sim-endpoint',
    [string] $AssetName = 'modbus-sim-asset',
    [string] $ModbusAddress = 'modbus-tcp://modbus-simulator.azure-iot-operations.svc.cluster.local:502',
    [string] $EndpointConfig = '{"unitId": 1, "timeoutMs": 2000, "connectTimeoutMs": 3000}'
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

Invoke-Az "Add Modbus endpoint $EndpointName -> $ModbusAddress" (@(
    'iot', 'ops', 'ns', 'device', 'endpoint', 'inbound', 'add', 'custom',
    '--device', $DeviceName, '--name', $EndpointName,
    '--endpoint-type', 'Custom.ModbusTcp', '--version', '1.0',
    '--endpoint-address', $ModbusAddress,
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
    '--destination', "topic=modbus/$DeviceName/$AssetName/process", 'qos=Qos1', 'retain=Never', 'ttl=3600') + $common)

# Dataset 2: slow identity values to the broker state store
Invoke-Az 'Add dataset identity' (@(
    'iot', 'ops', 'ns', 'asset', 'custom', 'dataset', 'add', '--asset', $AssetName, '--name', 'identity',
    '--config', (JsonFile 'ds-identity' '{"samplingIntervalMs": 30000}'),
    '--destination', "key=$AssetName-identity") + $common)

# Data points: dataset, name, Modbus address, data point configuration (see docs/modbus/CONFIGURATION.md)
$points = @(
    @('process', 'temperature', 'hr:0', '{"dataType": "Float32"}'),
    @('process', 'humidity', 'hr:2', '{"dataType": "UInt16", "scale": 0.1}'),
    @('process', 'cycleCounter', 'hr:3', '{"dataType": "UInt32"}'),
    @('process', 'pressureDelta', '40006', '{"dataType": "Int16"}'),
    @('process', 'running', 'hr:6', '{"bit": 0}'),
    @('process', 'alarm', 'hr:6', '{"bit": 1}'),
    @('process', 'flowRate', 'ir:0', '{"dataType": "Float32", "wordOrder": "LittleEndian"}'),
    @('process', 'tankLevelPercent', 'ir:2', '{"dataType": "UInt16", "scale": 0.024420024420024}'),
    @('process', 'pumpOn', 'coil:0', '{}'),
    @('process', 'valveOpen', 'coil:1', '{}'),
    @('process', 'doorClosed', 'di:0', '{}'),
    @('process', 'emergencyStop', 'di:1', '{}'),
    @('identity', 'serialNumber', 'hr:10', '{"dataType": "String", "length": 8}'),
    @('identity', 'totalEnergyKWh', 'hr:20', '{"dataType": "Float64"}')
)

foreach ($p in $points) {
    $dataset, $name, $source, $config = $p
    Invoke-Az "Add data point $dataset/$name ($source)" (@(
        'iot', 'ops', 'ns', 'asset', 'custom', 'datapoint', 'add', '--asset', $AssetName, '--dataset', $dataset,
        '--name', $name, '--data-source', $source, '--config', (JsonFile "dp-$name" $config), '--replace') + $common)
}

Write-Host ''
Write-Host "Done. Check: az iot ops ns asset custom dataset list --asset $AssetName -i $Instance -g $ResourceGroup -o table" -ForegroundColor Green
