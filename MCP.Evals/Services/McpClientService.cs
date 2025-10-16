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
/// </summary>
public class McpClientService : IMcpClientService
{
    private readonly ITransportResolver _transportResolver;
    private readonly ITransportFactory _transportFactory;
    private readonly IToolExecutionPlanner _toolExecutionPlanner;
    private readonly ILogger<McpClientService> _logger;
    private readonly bool _verboseLogging;

    public McpClientService(
        ITransportResolver transportResolver,
        ITransportFactory transportFactory,
        IToolExecutionPlanner toolExecutionPlanner,
        ILogger<McpClientService> logger)
    {
        _transportResolver = transportResolver;
        _transportFactory = transportFactory;
        _toolExecutionPlanner = toolExecutionPlanner;
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    /// <summary>
    /// Creates a client transport using the injected transport factory
    /// </summary>
    private async Task<IClientTransport> CreateClientTransportAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        var transportType = _transportResolver.ResolveTransportType(serverConfig);

        if (_verboseLogging)
        {
            _logger.LogInformation("Creating {TransportType} transport for server: {ServerPath}",
                transportType, serverConfig.Path ?? serverConfig.Url);
        }

        return await _transportFactory.CreateTransportAsync(transportType, serverConfig, cancellationToken);
    }


    public async Task<string> ExecuteToolInteractionAsync(
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (_verboseLogging)
            _logger.LogInformation("Starting tool interaction with server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path, serverConfig.Transport);

        try
        {
            // Use transport factory to create and connect
            var clientTransport = await CreateClientTransportAsync(serverConfig, cancellationToken);
            await using var client = await McpClient.CreateAsync(clientTransport);

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
        if (_verboseLogging)
            _logger.LogDebug("Testing connection to server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path, serverConfig.Transport);

        try
        {
            // Use transport factory for test connection
            var transportType = _transportResolver.ResolveTransportType(serverConfig);
            var clientTransport = await _transportFactory.CreateTransportAsync(transportType, serverConfig, cancellationToken);

            await using var client = await McpClient.CreateAsync(clientTransport);
            var tools = await client.ListToolsAsync();

            if (_verboseLogging)
                _logger.LogDebug("Connection test successful for server: {ServerPath}, found {ToolCount} tools",
                    serverConfig.Path, tools.Count);

            return tools.Any(); // Consider connection successful if we can list tools
        }
        catch (Exception ex)
        {
            if (_verboseLogging)
                _logger.LogWarning(ex, "Connection test failed for server: {ServerPath}", serverConfig.Path);
            return false;
        }
    }
}