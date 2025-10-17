using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Commands;
using MCP.Evals.Exceptions;
using MCP.Evals.Models;
using ModelContextProtocol.Client;
using System.Collections.Concurrent;

namespace MCP.Evals.Services;

/// <summary>
/// Manages MCP client connections per configuration to avoid recreation overhead
/// Implements connection pooling and reuse for better performance
/// </summary>
public class McpConnectionManager : IMcpConnectionManager
{
    private readonly ITransportResolutionService _transportResolver;
    private readonly ITransportCreationService _transportFactory;
    private readonly IServerProcessManager _serverProcessManager;
    private readonly ILogger<McpConnectionManager> _logger;
    private readonly bool _verboseLogging;

    // Cache connections by server configuration key
    private readonly ConcurrentDictionary<string, McpClient> _connectionCache = new();
    private bool _disposed = false;

    public McpConnectionManager(
        ITransportResolutionService transportResolver,
        ITransportCreationService transportFactory,
        IServerProcessManager serverProcessManager,
        ILogger<McpConnectionManager> logger,
        EvaluationCommandOptions? commandOptions = null)
    {
        _transportResolver = transportResolver;
        _transportFactory = transportFactory;
        _serverProcessManager = serverProcessManager;
        _logger = logger;
        _verboseLogging = commandOptions?.Verbose ?? false;
    }

    public async Task<McpClient> GetOrCreateClientAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        var configKey = GetConfigurationKey(serverConfig);

        if (_connectionCache.TryGetValue(configKey, out var existingClient))
        {
            if (_verboseLogging)
                _logger.LogDebug("Reusing existing MCP client for server: {ServerPath}",
                    serverConfig.Path ?? serverConfig.Url);
            return existingClient;
        }

        if (_verboseLogging)
            _logger.LogInformation("Creating new MCP client for server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path ?? serverConfig.Url, serverConfig.Transport);

        try
        {
            // Create transport using the existing factory
            var transportType = _transportResolver.ResolveTransportType(serverConfig);
            var clientTransport = await _transportFactory.CreateTransportAsync(transportType, serverConfig, cancellationToken);

            // Create and cache the client
            var client = await McpClient.CreateAsync(clientTransport);

            // Cache the client for reuse
            _connectionCache.TryAdd(configKey, client);

            if (_verboseLogging)
                _logger.LogInformation("Successfully created and cached MCP client for server: {ServerPath}",
                    serverConfig.Path ?? serverConfig.Url);

            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client for server: {ServerPath}",
                serverConfig.Path ?? serverConfig.Url);
            throw new McpClientException(serverConfig.Path ?? serverConfig.Url ?? "Unknown",
                $"Failed to create client connection: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        if (_verboseLogging)
            _logger.LogDebug("Testing connection to server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path ?? serverConfig.Url, serverConfig.Transport);

        try
        {
            var client = await GetOrCreateClientAsync(serverConfig, cancellationToken);
            var tools = await client.ListToolsAsync();

            if (_verboseLogging)
                _logger.LogDebug("Connection test successful for server: {ServerPath}, found {ToolCount} tools",
                    serverConfig.Path ?? serverConfig.Url, tools.Count);

            return tools.Any(); // Consider connection successful if we can list tools
        }
        catch (Exception ex)
        {
            if (_verboseLogging)
                _logger.LogWarning(ex, "Connection test failed for server: {ServerPath}",
                    serverConfig.Path ?? serverConfig.Url);
            return false;
        }
    }

    public async Task CloseAllConnectionsAsync()
    {
        if (_verboseLogging)
            _logger.LogInformation("Closing all MCP connections ({Count} active)", _connectionCache.Count);

        var closeTasks = new List<Task>();

        // Close all clients
        foreach (var kvp in _connectionCache)
        {
            closeTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await kvp.Value.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing client for key: {Key}", kvp.Key);
                }
            }));
        }

        await Task.WhenAll(closeTasks);

        _connectionCache.Clear();

        // Also stop all server processes
        await _serverProcessManager.StopAllServerProcessesAsync();

        if (_verboseLogging)
            _logger.LogInformation("All MCP connections and server processes closed successfully");
    }

    private static string GetConfigurationKey(ServerConfiguration serverConfig)
    {
        // Create a unique key based on server configuration
        var keyParts = new List<string>
        {
            serverConfig.Transport ?? "stdio",
            serverConfig.Path ?? string.Empty,
            serverConfig.Url ?? string.Empty
        };

        if (serverConfig.Args?.Any() == true)
        {
            keyParts.Add(string.Join("|", serverConfig.Args));
        }

        return string.Join(":", keyParts);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            CloseAllConnectionsAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during dispose");
        }

        _disposed = true;
    }
}