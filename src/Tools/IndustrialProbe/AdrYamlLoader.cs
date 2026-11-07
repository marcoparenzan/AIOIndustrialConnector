// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using YamlDotNet.Serialization;

namespace AioIndustrialConnector.Probe;

/// <summary>
/// Reads the Kubernetes manifests of an Azure Device Registry <c>Device</c> and <c>Asset</c>
/// (the same files used with kubectl) into the SDK models the connector receives at runtime.
/// </summary>
/// <remarks>Only the fields the connectors use are mapped.</remarks>
internal static class AdrYamlLoader
{
    public static (string Name, Device Device) LoadDevice(string path)
    {
        JsonNode root = ReadYaml(path, "Device");
        JsonNode spec = root["spec"] ?? throw new InvalidDataException($"{path}: missing spec.");
        string name = root["metadata"]?["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);

        var inbound = new Dictionary<string, InboundEndpointSchemaMapValue>();
        if (spec["endpoints"]?["inbound"] is JsonObject endpoints)
        {
            foreach ((string endpointName, JsonNode? endpoint) in endpoints)
            {
                inbound[endpointName] = new InboundEndpointSchemaMapValue
                {
                    Address = Str(endpoint?["address"]) ?? "",
                    EndpointType = Str(endpoint?["endpointType"]) ?? "",
                    Version = Str(endpoint?["version"]),
                    AdditionalConfiguration = Str(endpoint?["additionalConfiguration"]),
                };
            }
        }

        return (name, new Device { Enabled = true, Endpoints = new DeviceEndpoints { Inbound = inbound } });
    }

    public static (string Name, Asset Asset) LoadAsset(string path)
    {
        JsonNode root = ReadYaml(path, "Asset");
        JsonNode spec = root["spec"] ?? throw new InvalidDataException($"{path}: missing spec.");
        string name = root["metadata"]?["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);

        var datasets = new List<AssetDataset>();
        foreach (JsonNode? ds in spec["datasets"]?.AsArray() ?? [])
        {
            datasets.Add(new AssetDataset
            {
                Name = Str(ds?["name"]) ?? throw new InvalidDataException($"{path}: dataset without name."),
                DataSource = Str(ds?["dataSource"]),
                DatasetConfiguration = Str(ds?["datasetConfiguration"]),
                Destinations = ds?["destinations"]?.AsArray().Select(d => new DatasetDestination
                {
                    Target = Enum.TryParse(Str(d?["target"]), true, out DatasetTarget t) ? t : DatasetTarget.Mqtt,
                    Configuration = new DestinationConfiguration
                    {
                        Topic = Str(d?["configuration"]?["topic"]),
                        Key = Str(d?["configuration"]?["key"]),
                    },
                }).ToList(),
                DataPoints = ds?["dataPoints"]?.AsArray().Select(dp => new AssetDatasetDataPoint
                {
                    Name = Str(dp?["name"]),
                    DataSource = Str(dp?["dataSource"]),
                    DataPointConfiguration = Str(dp?["dataPointConfiguration"]),
                }).ToList(),
            });
        }

        var asset = new Asset
        {
            DeviceRef = new AssetDeviceRef
            {
                DeviceName = Str(spec["deviceRef"]?["deviceName"]) ?? "",
                EndpointName = Str(spec["deviceRef"]?["endpointName"]) ?? "",
            },
            DefaultDatasetsConfiguration = Str(spec["defaultDatasetsConfiguration"]),
            Datasets = datasets,
        };

        return (name, asset);
    }

    private static JsonNode ReadYaml(string path, string expectedKind)
    {
        using var reader = File.OpenText(path);
        object? yaml = new DeserializerBuilder().Build().Deserialize(reader);
        string json = new SerializerBuilder().JsonCompatible().Build().Serialize(yaml);
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidDataException($"{path} is empty.");

        string? kind = Str(root["kind"]);
        if (!string.Equals(kind, expectedKind, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: expected kind '{expectedKind}', found '{kind}'.");
        }

        return root;
    }

    /// <summary>YAML scalars come through as JSON strings (quoted or not), so read every scalar as text.</summary>
    private static string? Str(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v => v.ToString(),
        _ => node.ToJsonString(),
    };
}
