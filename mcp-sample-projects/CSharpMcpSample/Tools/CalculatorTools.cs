using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CSharpMcpSample.Tools;

/// <summary>
/// Sample MCP tools for evaluation and comparison purposes.
/// These tools provide equivalent functionality to the TypeScript sample server.
/// </summary>
internal class CalculatorTools
{
    [McpServerTool]
    [Description("Add two numbers together")]
    public AddResult Add(
        [Description("First number")] double a,
        [Description("Second number")] double b)
    {
        var result = a + b;
        Console.Error.WriteLine($"C# MCP: Adding {a} + {b} = {result}");
        return new AddResult { Result = result };
    }

    [McpServerTool]
    [Description("Multiply two numbers together")]
    public MultiplyResult Multiply(
        [Description("First number")] double a,
        [Description("Second number")] double b)
    {
        var result = a * b;
        Console.Error.WriteLine($"C# MCP: Multiplying {a} * {b} = {result}");
        return new MultiplyResult { Result = result };
    }

    [McpServerTool]
    [Description("Echo back the provided message")]
    public EchoResult Echo(
        [Description("Message to echo")] string message)
    {
        var echo = $"C# Echo: {message}";
        Console.Error.WriteLine($"C# MCP: Echoing \"{message}\"");
        return new EchoResult { Echo = echo };
    }
}