#!/bin/bash
# Development deployment of one protocol mode to a local k3d cluster running Azure IoT Operations
# (the environment described in the iot-operations-sdks doc/setup.md).
# Builds the connector and simulator images as tarballs with the .NET SDK (no Docker daemon needed for the build),
# imports them into k3d and applies the manifests in deploy/<protocol>/k8s with kubectl.
#
# Usage: ./deploy/deploy-k3d.sh <modbus|s7> [k3d-cluster-name]   (default cluster: k3s-default)

set -euo pipefail

PROTOCOL="${1:-}"
case "$PROTOCOL" in
  modbus) SIM_PROJECT="src/Modbus/ModbusSimulator/ModbusSimulator.csproj" ;;
  s7)     SIM_PROJECT="src/S7/S7Simulator/S7Simulator.csproj" ;;
  *) echo "Usage: $0 <modbus|s7> [k3d-cluster-name]" >&2; exit 2 ;;
esac
CLUSTER="${2:-k3s-default}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
# Output: $BUILD_ARTIFACTS, else /d/Dev/BuildArtifacts/AIOIndustrialConnector when present, else <repo>/artifacts (git-ignored).
if [ -n "${BUILD_ARTIFACTS:-}" ]; then ART="$BUILD_ARTIFACTS"
elif [ -d /d/Dev/BuildArtifacts ]; then ART=/d/Dev/BuildArtifacts/AIOIndustrialConnector
else ART="$REPO/artifacts"; fi
OUT="$ART/images"
mkdir -p "$OUT"

echo "==> Building $PROTOCOL container images as tarballs in $OUT"
dotnet publish "$REPO/src/Host/IndustrialConnector/IndustrialConnector.csproj" -c Release -r linux-x64 \
  -t:PublishContainer -p:ConnectorProtocol="$PROTOCOL" -p:ContainerArchiveOutputPath="$OUT/aio-$PROTOCOL-connector.tar.gz"
dotnet publish "$REPO/$SIM_PROJECT" -c Release -r linux-x64 \
  -t:PublishContainer -p:ContainerArchiveOutputPath="$OUT/aio-$PROTOCOL-simulator.tar.gz"

echo "==> Importing images into k3d cluster $CLUSTER"
k3d image import "$OUT/aio-$PROTOCOL-connector.tar.gz" "$OUT/aio-$PROTOCOL-simulator.tar.gz" -c "$CLUSTER"

echo "==> Deploying simulator, connector template, device and asset"
kubectl apply -f "$REPO/deploy/$PROTOCOL/k8s/simulator.yaml"
kubectl apply -f "$REPO/deploy/$PROTOCOL/k8s/connector-template.yaml"
kubectl apply -f "$REPO/deploy/$PROTOCOL/k8s/device.yaml"
kubectl apply -f "$REPO/deploy/$PROTOCOL/k8s/asset.yaml"

echo
echo "Watch the connector pod:  kubectl get pods -n azure-iot-operations -w | grep $PROTOCOL"
echo "Watch the data:           mosquitto_sub -L mqtt://localhost/$PROTOCOL/#"
