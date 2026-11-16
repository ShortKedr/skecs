namespace Skecs.Agentic.Tests;

public sealed class RequestTests
{
    [Fact]
    public void ParsesOperationPositionalsAndOptions()
    {
        var request = Request.Parse(["validate", "space-battle", "--configuration", "Debug", "--timeout-seconds", "42"]);

        Assert.Equal("validate", request.Operation);
        Assert.Equal("Debug", request.Configuration);
        Assert.Equal(42, request.TimeoutSeconds);
        Assert.Equal(["space-battle"], request.Arguments);
    }

    [Fact]
    public void UsesDefaultsAndSupportsPositionalsBeforeOptions()
    {
        var request = Request.Parse(["validate", "space-battle"]);

        Assert.Equal("Release", request.Configuration);
        Assert.Equal(300, request.TimeoutSeconds);
        Assert.Equal(["space-battle"], request.Arguments);
    }

    [Fact]
    public void RequiresAnOperation()
    {
        Assert.Throws<ArgumentException>(() => Request.Parse([]));
        Assert.Throws<ArgumentException>(() => Request.Parse(["--timeout-seconds", "10"]));
    }

    [Theory]
    [InlineData("validate", "--unknown")]
    [InlineData("validate", "--timeout-seconds")]
    [InlineData("validate", "--configuration")]
    [InlineData("validate", "--timeout-seconds", "10", "--timeout-seconds", "20")]
    [InlineData("validate", "--configuration", "Release", "--configuration", "Debug")]
    [InlineData("validate", "--configuration", "Debug", "second")]
    [InlineData("validate", "--timeout-seconds", "+1")]
    [InlineData("validate", "--timeout-seconds", "01")]
    [InlineData("validate", "--timeout-seconds", "1.0")]
    [InlineData("validate", "--timeout-seconds", " 1")]
    [InlineData("validate", "--timeout-seconds", "0")]
    [InlineData("validate", "--timeout-seconds", "3601")]
    [InlineData("validate", "--configuration", "RelWithDebInfo")]
    [InlineData("validate", "--configuration", "release")]
    public void RejectsMalformedOrUnsupportedArguments(params string[] args)
        => Assert.Throws<ArgumentException>(() => Request.Parse(args));
}
