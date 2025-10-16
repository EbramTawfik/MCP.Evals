using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Exceptions;
using MCP.Evals.Models;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Diagnostics;

namespace MCP.Evals.Services;

/// <summary>
/// Creates MCP client transports for different connection types
/// </summary>
public class TransportCreationService : ITransportCreationService
{
    private readonly IServerTypeDetectionService _serverTypeDetector;
    private readonly IServerProcessManagementService _processManager;
    private readonly ILogger<TransportCreationService> _logger;
    private readonly List<Process> _runningProcesses;
    private readonly bool _verboseLogging;

    public TransportCreationService(
        IServerTypeDetectionService serverTypeDetector,
        IServerProcessManagementService processManager,
        ILogger<TransportCreationService> logger)
    {
        _serverTypeDetector = serverTypeDetector;
        _processManager = processManager;
        _logger = logger;
        _runningProcesses = new List<Process>();
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    public async Task<IClientTransport> CreateTransportAsync(
        string transportType,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        return transportType.ToLowerInvariant() switch
        {
            "http" => await CreateHttpTransportAsync(serverConfig, cancellationToken),
            "stdio" => CreateStdioTransport(serverConfig),
            _ => throw new ArgumentException($"Unsupported transport type: {transportType}")
        };
    }

    private async Task<IClientTransport> CreateHttpTransportAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken)
    {
        // Validate URL is provided
        if (string.IsNullOrEmpty(serverConfig.Url))
        {
            throw new ArgumentException("HTTP transport requires a 'url' field in server configuration");
        }

        if (!Uri.TryCreate(serverConfig.Url, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != "http" && serverUri.Scheme != "https"))
        {
            throw new ArgumentException($"Invalid HTTP URL: {serverConfig.Url}");
        }

        // If no path is specified, assume server is already running
        if (string.IsNullOrEmpty(serverConfig.Path))
        {
            if (_verboseLogging)
            {
                _logger.LogInformation("Using direct HTTP connection to: {Url}", serverConfig.Url);
            }

            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "HttpServer",
                Endpoint = serverUri
            });
        }

        // Start server and then connect
        var serverPath = Path.GetFullPath(serverConfig.Path);
        var serverType = _serverTypeDetector.DetectServerType(serverPath, serverConfig);

        if (_verboseLogging)
        {
            _logger.LogInformation("Starting {ServerType} HTTP server from: {ServerPath} and connecting to: {Url}",
                serverType, serverPath, serverConfig.Url);
        }

        var serverProcess = await _processManager.StartServerAsync(
            serverType, serverPath, serverConfig, cancellationToken);

        try
        {
            // Wait for server to be ready
            var isReady = await _processManager.IsServerReadyAsync(serverConfig.Url, cancellationToken);
            if (!isReady)
            {
                serverProcess.Kill();
                throw new McpClientException(serverPath, "HTTP server failed to start or become ready");
            }

            // Store process for cleanup
            _runningProcesses.Add(serverProcess);

            if (_verboseLogging)
            {
                _logger.LogInformation("HTTP server started and connected successfully");
            }

            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "HttpServer",
                Endpoint = serverUri
            });
        }
        catch
        {
            serverProcess.Kill();
            throw;
        }
    }

    private IClientTransport CreateStdioTransport(ServerConfiguration serverConfig)
    {
        if (string.IsNullOrEmpty(serverConfig.Path))
        {
            throw new ArgumentException("Stdio transport requires a 'path' field in server configuration");
        }

        var serverPath = Path.GetFullPath(serverConfig.Path);
        var serverType = _serverTypeDetector.DetectServerType(serverPath, serverConfig);

        if (_verboseLogging)
        {
            _logger.LogInformation("Creating stdio transport for {ServerType}: {ServerPath}",
                serverType, serverPath);
        }

        return serverType switch
        {
            ServerType.TypeScriptScript => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "TypeScriptServer",
                Command = "npx",
                Arguments = ["tsx", serverPath, .. (serverConfig.Args ?? Array.Empty<string>())]
            }),

            ServerType.NodeScript => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "NodeServer",
                Command = "node",
                Arguments = [serverPath, .. (serverConfig.Args ?? Array.Empty<string>())]
            }),

            ServerType.CSharpExecutable => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "CSharpServer",
                Command = serverPath,
                Arguments = serverConfig.Args ?? Array.Empty<string>()
            }),

            ServerType.PythonScript => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "PythonServer",
                Command = "python",
                Arguments = [serverPath, .. (serverConfig.Args ?? Array.Empty<string>())]
            }),

            _ => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "DefaultServer",
                Command = serverPath,
                Arguments = serverConfig.Args ?? Array.Empty<string>()
            })
        };
    }
}