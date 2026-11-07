// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Text.Json.Nodes;
using AioIndustrialConnector.Core.Planning;

namespace AioIndustrialConnector.Core.Schema;

/// <summary>
/// Derives the JSON Schema (draft-07) of a dataset payload from its plan. Identical for every protocol because the
/// payload shape is: a flat object with one property per data point.
/// </summary>
public static class JsonSchemaBuilder
{
    public static string Build(IDatasetPlan plan)
    {
        var properties = new JsonObject();
        foreach (IDataPoint point in plan.Points)
        {
            string type = point.JsonType switch
            {
                JsonValueType.Boolean => "boolean",
                JsonValueType.String => "string",
                JsonValueType.Number => "number",
                _ => "integer",
            };

            // null is allowed because a point is published as null when the device rejects its read request.
            properties[point.Name] = new JsonObject
            {
                ["type"] = new JsonArray(type, "null"),
                ["description"] = point.Describe(),
            };
        }

        var schema = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["type"] = "object",
            ["properties"] = properties,
        };

        return schema.ToJsonString();
    }
}
