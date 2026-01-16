// =============================================================================
// AzureOutputCaptureTests.cs
//
// Summary: Unit tests for AzureOutputCapture class.
//
// Tests verify that:
// - Standard deployment outputs are parsed correctly
// - Missing outputs are handled gracefully
// - Nested output values are extracted
// - Different value types (string, number, boolean) are handled
// - Invalid JSON doesn't throw
//
// Design: Uses BicepDeployment directly and TestLogger to verify behavior.
// =============================================================================

using Ando.Operations;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AzureOutputCaptureTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public void CaptureDeploymentOutputs_ParsesStandardFormat()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "storageAccountName": {
                    "type": "String",
                    "value": "mystorageaccount"
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal("mystorageaccount", deployment.GetOutput("storageAccountName"));
    }

    [Fact]
    public void CaptureDeploymentOutputs_ParsesMultipleOutputs()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "output1": { "type": "String", "value": "value1" },
                "output2": { "type": "String", "value": "value2" },
                "output3": { "type": "String", "value": "value3" }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal("value1", deployment.GetOutput("output1"));
        Assert.Equal("value2", deployment.GetOutput("output2"));
        Assert.Equal("value3", deployment.GetOutput("output3"));
        Assert.Equal(3, deployment.OutputCount);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesEmptyOutput()
    {
        var deployment = new BicepDeployment();

        AzureOutputCapture.CaptureDeploymentOutputs("", deployment, _logger);

        Assert.Equal(0, deployment.OutputCount);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesWhitespaceOutput()
    {
        var deployment = new BicepDeployment();

        AzureOutputCapture.CaptureDeploymentOutputs("   \n  ", deployment, _logger);

        Assert.Equal(0, deployment.OutputCount);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesEmptyOutputsObject()
    {
        var deployment = new BicepDeployment();
        var json = "{}";

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal(0, deployment.OutputCount);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesNumberValue()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "portNumber": {
                    "type": "Int",
                    "value": 443
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal("443", deployment.GetOutput("portNumber"));
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesBooleanValue()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "isEnabled": {
                    "type": "Bool",
                    "value": true
                },
                "isDisabled": {
                    "type": "Bool",
                    "value": false
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal("true", deployment.GetOutput("isEnabled"));
        Assert.Equal("false", deployment.GetOutput("isDisabled"));
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesNestedResponse()
    {
        var deployment = new BicepDeployment();
        // Full ARM deployment response format
        var json = """
            {
                "properties": {
                    "outputs": {
                        "serverFqdn": {
                            "type": "String",
                            "value": "myserver.database.windows.net"
                        }
                    }
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal("myserver.database.windows.net", deployment.GetOutput("serverFqdn"));
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesObjectValue()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "complexOutput": {
                    "type": "Object",
                    "value": {
                        "nested": "data",
                        "count": 42
                    }
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        // Object values are stored as raw JSON
        var output = deployment.GetOutput("complexOutput");
        Assert.NotNull(output);
        Assert.Contains("nested", output);
        Assert.Contains("42", output);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesArrayValue()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "ipAddresses": {
                    "type": "Array",
                    "value": ["10.0.0.1", "10.0.0.2"]
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        // Array values are stored as raw JSON
        var output = deployment.GetOutput("ipAddresses");
        Assert.NotNull(output);
        Assert.Contains("10.0.0.1", output);
        Assert.Contains("10.0.0.2", output);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesInvalidJson_DoesNotThrow()
    {
        var deployment = new BicepDeployment();
        var invalidJson = "{ not valid json";

        // Should not throw
        AzureOutputCapture.CaptureDeploymentOutputs(invalidJson, deployment, _logger);

        Assert.Equal(0, deployment.OutputCount);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesSecureString()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "sqlPassword": {
                    "type": "SecureString",
                    "value": "SuperSecretPassword123!"
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Equal("SuperSecretPassword123!", deployment.GetOutput("sqlPassword"));
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesNullValue()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "optionalOutput": {
                    "type": "String",
                    "value": null
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        // Null values are not stored
        Assert.Null(deployment.GetOutput("optionalOutput"));
    }

    [Fact]
    public void OutputRef_ResolvesValue()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "connectionString": {
                    "type": "String",
                    "value": "Server=myserver;..."
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        var outputRef = deployment.Output("connectionString");
        Assert.Equal("connectionString", outputRef.Name);
        Assert.Equal("Server=myserver;...", outputRef.Resolve());
    }

    [Fact]
    public void OutputRef_ReturnsNullForMissingOutput()
    {
        var deployment = new BicepDeployment();

        var outputRef = deployment.Output("nonExistent");

        Assert.Null(outputRef.Resolve());
    }

    [Fact]
    public void OutputRef_ToStringReturnsPlaceholder()
    {
        var deployment = new BicepDeployment();
        var outputRef = deployment.Output("myOutput");

        Assert.Equal("{deployment.myOutput}", outputRef.ToString());
    }

    [Fact]
    public void OutputNames_ReturnsAllCapturedNames()
    {
        var deployment = new BicepDeployment();
        var json = """
            {
                "output1": { "type": "String", "value": "value1" },
                "output2": { "type": "String", "value": "value2" }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, deployment, _logger);

        Assert.Contains("output1", deployment.OutputNames);
        Assert.Contains("output2", deployment.OutputNames);
    }
}
