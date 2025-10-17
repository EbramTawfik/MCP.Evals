using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Exceptions;
using MCP.Evals.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MCP.Evals.Services;

/// <summary>
/// Manages server processes to ensure they are reused per configuration rather than recreated
/// </summary>
public class ServerProcessManager : IServerProcessManager
{
    private readonly IServerTypeDetectionService _serverTypeDetector;
    private readonly IServerProcessManagementService _processManager;
    private readonly ILogger<ServerProcessManager> _logger;
    private readonly bool _verboseLogging;

    // Cache running processes by server configuration key
    private readonly ConcurrentDictionary<string, Process> _processCache = new();
    private bool _disposed = false;

    public ServerProcessManager(
        IServerTypeDetectionService serverTypeDetector,
        IServerProcessManagementService processManager,
        ILogger<ServerProcessManager> logger)
    {
        _serverTypeDetector = serverTypeDetector;
        _processManager = processManager;
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    public bool IsServerProcessRequired(ServerConfiguration serverConfig)
    {
        // Server process is required for HTTP transport when a path is specified
        // STDIO transport handles process management internally in the transport
        return serverConfig.Transport?.ToLowerInvariant() == "http" &&
               !string.IsNullOrEmpty(serverConfig.Path);
    }

    public async Task<Process?> GetOrStartServerProcessAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        if (!IsServerProcessRequired(serverConfig))
        {
            return null; // No server process needed
        }

        var configKey = GetConfigurationKey(serverConfig);

        // Check if we already have a running process for this configuration
        if (_processCache.TryGetValue(configKey, out var existingProcess))
        {
            if (!existingProcess.HasExited)
            {
                if (_verboseLogging)
                    _logger.LogDebug("Reusing existing server process for: {ServerPath}",
                        serverConfig.Path);
                return existingProcess;
            }
            else
            {
                // Process has exited, remove it from cache
                _processCache.TryRemove(configKey, out _);
                if (_verboseLogging)
                    _logger.LogWarning("Cached server process has exited, will start a new one for: {ServerPath}",
                        serverConfig.Path);
            }
        }

        // Start a new server process
        if (_verboseLogging)
            _logger.LogInformation("Starting new server process for: {ServerPath} (Transport: {Transport})",
                serverConfig.Path, serverConfig.Transport);

        try
        {
            var serverPath = Path.GetFullPath(serverConfig.Path!);
            var serverType = _serverTypeDetector.DetectServerType(serverPath, serverConfig);

            var process = await _processManager.StartServerAsync(
                serverType, serverPath, serverConfig, cancellationToken);

            // Cache the process
            _processCache.TryAdd(configKey, process);

            // For HTTP servers, wait until they're ready
            if (!string.IsNullOrEmpty(serverConfig.Url))
            {
                var isReady = await _processManager.IsServerReadyAsync(serverConfig.Url, cancellationToken);
                if (!isReady)
                {
                    process.Kill();
                    _processCache.TryRemove(configKey, out _);
                    throw new McpClientException(serverPath, "HTTP server failed to start or become ready");
                }
            }

            if (_verboseLogging)
                _logger.LogInformation("Successfully started and cached server process for: {ServerPath}",
                    serverConfig.Path);

            return process;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start server process for: {ServerPath}", serverConfig.Path);
            throw new McpClientException(serverConfig.Path ?? "Unknown",
                $"Failed to start server process: {ex.Message}");
        }
    }

    public async Task StopAllServerProcessesAsync()
    {
        if (_verboseLogging)
            _logger.LogInformation("Stopping all server processes ({Count} active)", _processCache.Count);

        var stopTasks = new List<Task>();

        foreach (var kvp in _processCache)
        {
            stopTasks.Add(Task.Run(() =>
            {
                try
                {
                    if (!kvp.Value.HasExited)
                    {
                        kvp.Value.Kill();
                        kvp.Value.WaitForExit(5000); // Wait up to 5 seconds for graceful exit
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping server process for key: {Key}", kvp.Key);
                }
            }));
        }

        await Task.WhenAll(stopTasks);

        _processCache.Clear();

        if (_verboseLogging)
            _logger.LogInformation("All server processes stopped successfully");
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
            StopAllServerProcessesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during dispose");
        }

        _disposed = true;
    }
}