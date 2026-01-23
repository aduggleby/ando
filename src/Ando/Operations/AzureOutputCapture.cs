// =============================================================================
// AzureOutputCapture.cs
//
// Summary: Parses Azure deployment outputs and stores them in BicepDeployment.
//
// AzureOutputCapture handles the JSON output from Azure CLI deployment commands
// and extracts the deployment outputs into a BicepDeployment object for use in
// subsequent build steps (e.g., passing a SQL connection string to EF migrations).
//
// Azure deployment outputs have this structure:
// {
//   "outputName": {
//     "type": "String",
//     "value": "actual-value"
//   }
// }
//
// Design Decisions:
// - Uses System.Text.Json for parsing (no external dependencies)
// - Flattens nested output structure to simple key-value pairs
// - Stores outputs in BicepDeployment._outputs dictionary
// - Handles SecureString outputs by using the value directly (already in JSON)
// - Logs captured outputs for debugging visibility
// =============================================================================

using System.Text.Json;
using Ando.Logging;

namespace Ando.Operations;

/// <summary>
/// Parses Azure deployment outputs and stores them in BicepDeployment.
/// </summary>
public static class AzureOutputCapture
{
    /// <summary>
    /// Parses deployment outputs from Azure CLI JSON output and stores in BicepDeployment.
    /// </summary>
    /// <param name="jsonOutput">Raw JSON output from az deployment command.</param>
    /// <param name="deployment">BicepDeployment to store outputs in.</param>
    /// <param name="logger">Logger for debug output.</param>
    public static void CaptureDeploymentOutputs(string jsonOutput, BicepDeployment deployment, IBuildLogger logger)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            logger.Debug("No deployment outputs to capture (empty output)");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            // Handle both direct outputs object and wrapped response format
            // Format 1 (--query properties.outputs): { "outputName": { "type": "...", "value": "..." } }
            // Format 2 (full response): { "properties": { "outputs": { ... } } }
            var outputs = root;
            if (root.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("outputs", out var nestedOutputs))
            {
                outputs = nestedOutputs;
            }

            var capturedCount = 0;
            foreach (var output in outputs.EnumerateObject())
            {
                var outputName = output.Name;
                var outputValue = ExtractOutputValue(output.Value);

                if (outputValue != null)
                {
                    deployment._outputs[outputName] = outputValue;
                    capturedCount++;
                    logger.Debug($"  Captured output: {outputName}");
                }
            }

            if (capturedCount > 0)
            {
                logger.Info($"Captured {capturedCount} deployment output(s)");
            }
            else
            {
                logger.Debug("No deployment outputs found in response");
            }
        }
        catch (JsonException ex)
        {
            logger.Warning($"Failed to parse deployment outputs: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the value from an Azure deployment output object.
    /// </summary>
    private static string? ExtractOutputValue(JsonElement outputElement)
    {
        // Standard format: { "type": "String", "value": "actual-value" }
        if (outputElement.TryGetProperty("value", out var valueElement))
        {
            return valueElement.ValueKind switch
            {
                JsonValueKind.String => valueElement.GetString(),
                JsonValueKind.Number => valueElement.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                // For objects/arrays, return raw JSON string
                JsonValueKind.Object or JsonValueKind.Array => valueElement.GetRawText(),
                _ => null
            };
        }

        // If it's a direct value (not wrapped in type/value structure)
        return outputElement.ValueKind switch
        {
            JsonValueKind.String => outputElement.GetString(),
            JsonValueKind.Number => outputElement.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
