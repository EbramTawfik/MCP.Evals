using MCP.Evals.Models;
using ModelContextProtocol.Client;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for tool execution planning service
/// </summary>
public interface IToolExecutionPlanningService
{
    /// <summary>
    /// Determines which tools to execute based on a prompt and available tools
    /// </summary>
    Task<IReadOnlyList<ToolExecution>> PlanToolExecutionsAsync(
        string prompt,
        IList<ModelContextProtocol.Client.McpClientTool> availableTools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes tool interaction workflow with planning and execution
    /// </summary>
    Task<string> ExecuteToolInteractionAsync(
        ModelContextProtocol.Client.McpClient client,
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default);
}