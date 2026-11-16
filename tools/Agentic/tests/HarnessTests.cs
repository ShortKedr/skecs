using System.Text.Json;

namespace Skecs.Agentic.Tests;

public sealed class HarnessTests
{
    [Fact]
    public async Task ListsUnavailableModuleAndExplainsWhy()
    {
        var (exit, json) = await Run("list");
        using var result = JsonDocument.Parse(json);
        Assert.Equal(0, exit);
        Assert.Equal(1, result.RootElement.GetProperty("schemaVersion").GetInt32());
        var operation = Assert.Single(result.RootElement.GetProperty("operations").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "ga-check");
        Assert.False(operation.GetProperty("available").GetBoolean());
        Assert.Contains("Vulkan", operation.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("ga-check", 3, "unavailable")]
    [InlineData("dotnet", 2, "invalidRequest")]
    public async Task RejectsExecutionBeforeStartingTools(string operation, int code, string status)
    {
        var (exit, json) = await Run("run", operation);
        using var result = JsonDocument.Parse(json);
        Assert.Equal(code, exit);
        Assert.Equal(status, result.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("resultFile").ValueKind);
        Assert.Empty(result.RootElement.GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public async Task RejectsAnUnexpectedArgumentBeforeStartingTools()
    {
        var (exit, json) = await Run("run", "benchmark-list", "invalid-suite");
        using var result = JsonDocument.Parse(json);
        Assert.Equal(2, exit);
        Assert.Empty(result.RootElement.GetProperty("steps").EnumerateArray());
    }

    private static Task<(int ExitCode, string Json)> Run(params string[] args) =>
        Program.ExecuteAsync(args, AppContext.BaseDirectory, CancellationToken.None);
}
