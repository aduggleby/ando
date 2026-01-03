// =============================================================================
// AzureOutputCaptureTests.cs
//
// Summary: Unit tests for AzureOutputCapture class.
//
// Tests verify that:
// - Standard deployment outputs are parsed correctly
// - Prefix is applied to variable names
// - Missing outputs are handled gracefully
// - Nested output values are extracted
// - Different value types (string, number, boolean) are handled
// - Invalid JSON doesn't throw
//
// Design: Uses VarsContext directly and TestLogger to verify behavior.
// =============================================================================

using Ando.Context;
using Ando.Operations;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AzureOutputCaptureTests
{
    private readonly VarsContext _vars = new();
    private readonly TestLogger _logger = new();

    [Fact]
    public void CaptureDeploymentOutputs_ParsesStandardFormat()
    {
        var json = """
            {
                "storageAccountName": {
                    "type": "String",
                    "value": "mystorageaccount"
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("mystorageaccount", _vars["storageAccountName"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_ParsesMultipleOutputs()
    {
        var json = """
            {
                "output1": { "type": "String", "value": "value1" },
                "output2": { "type": "String", "value": "value2" },
                "output3": { "type": "String", "value": "value3" }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("value1", _vars["output1"]);
        Assert.Equal("value2", _vars["output2"]);
        Assert.Equal("value3", _vars["output3"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_AppliesPrefix()
    {
        var json = """
            {
                "connectionString": {
                    "type": "String",
                    "value": "Server=myserver;..."
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, "azure_", _logger);

        Assert.Equal("Server=myserver;...", _vars["azure_connectionString"]);
        Assert.Null(_vars["connectionString"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesEmptyOutput()
    {
        AzureOutputCapture.CaptureDeploymentOutputs("", _vars, null, _logger);

        Assert.Empty(_vars.All());
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesWhitespaceOutput()
    {
        AzureOutputCapture.CaptureDeploymentOutputs("   \n  ", _vars, null, _logger);

        Assert.Empty(_vars.All());
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesEmptyOutputsObject()
    {
        var json = "{}";

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Empty(_vars.All());
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesNumberValue()
    {
        var json = """
            {
                "portNumber": {
                    "type": "Int",
                    "value": 443
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("443", _vars["portNumber"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesBooleanValue()
    {
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

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("true", _vars["isEnabled"]);
        Assert.Equal("false", _vars["isDisabled"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesNestedResponse()
    {
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

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("myserver.database.windows.net", _vars["serverFqdn"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesObjectValue()
    {
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

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        // Object values are stored as raw JSON
        Assert.Contains("nested", _vars["complexOutput"]!);
        Assert.Contains("42", _vars["complexOutput"]!);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesArrayValue()
    {
        var json = """
            {
                "ipAddresses": {
                    "type": "Array",
                    "value": ["10.0.0.1", "10.0.0.2"]
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        // Array values are stored as raw JSON
        Assert.Contains("10.0.0.1", _vars["ipAddresses"]!);
        Assert.Contains("10.0.0.2", _vars["ipAddresses"]!);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesInvalidJson_DoesNotThrow()
    {
        var invalidJson = "{ not valid json";

        // Should not throw
        AzureOutputCapture.CaptureDeploymentOutputs(invalidJson, _vars, null, _logger);

        Assert.Empty(_vars.All());
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesSecureString()
    {
        var json = """
            {
                "sqlPassword": {
                    "type": "SecureString",
                    "value": "SuperSecretPassword123!"
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("SuperSecretPassword123!", _vars["sqlPassword"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_HandlesNullValue()
    {
        var json = """
            {
                "optionalOutput": {
                    "type": "String",
                    "value": null
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        // Null values are not stored
        Assert.Null(_vars["optionalOutput"]);
    }

    [Fact]
    public void CaptureDeploymentOutputs_PreservesExistingVars()
    {
        _vars["existingVar"] = "existing-value";

        var json = """
            {
                "newVar": {
                    "type": "String",
                    "value": "new-value"
                }
            }
            """;

        AzureOutputCapture.CaptureDeploymentOutputs(json, _vars, null, _logger);

        Assert.Equal("existing-value", _vars["existingVar"]);
        Assert.Equal("new-value", _vars["newVar"]);
    }
}
