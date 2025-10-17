using Microsoft.Extensions.Logging;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace MCP.Evals.Services;

/// <summary>
/// Refactored MCP client service following SOLID principles
/// Orchestrates other focused services to handle MCP operations
/// Now uses connection manager for efficient connection reuse
/// </summary>
public class McpClientService : IMcpClientService
{
    private readonly IMcpConnectionManager _connectionManager;
    private readonly IToolExecutionPlanningService _toolExecutionPlanner;
    private readonly ILogger<McpClientService> _logger;
    private readonly bool _verboseLogging;

    public McpClientService(
        IMcpConnectionManager connectionManager,
        IToolExecutionPlanningService toolExecutionPlanner,
        ILogger<McpClientService> logger)
    {
        _connectionManager = connectionManager;
        _toolExecutionPlanner = toolExecutionPlanner;
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    public async Task<string> ExecuteToolInteractionAsync(
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (_verboseLogging)
            _logger.LogInformation("Starting tool interaction with server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path ?? serverConfig.Url, serverConfig.Transport);

        try
        {
            // Use connection manager to get reusable client
            var client = await _connectionManager.GetOrCreateClientAsync(serverConfig, cancellationToken);

            // Delegate tool interaction planning and execution to focused service
            return await _toolExecutionPlanner.ExecuteToolInteractionAsync(
                client,
                serverConfig,
                prompt,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool interaction with server: {ServerPath}", serverConfig.Path ?? serverConfig.Url);
            throw new McpClientException(serverConfig.Path ?? serverConfig.Url ?? "Unknown", $"Failed to execute tool interaction: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        // Delegate to connection manager which handles caching and reuse
        return await _connectionManager.TestConnectionAsync(serverConfig, cancellationToken);
    }
}