using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Commands;
using MCP.Evals.Exceptions;
using MCP.Evals.Models;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Diagnostics;

namespace MCP.Evals.Services;

/// <summary>
/// Creates MCP client transports for different connection types
/// Now uses ServerProcessManager to avoid duplicate server processes
/// </summary>
public class TransportCreationService : ITransportCreationService
{
    private readonly IServerTypeDetectionService _serverTypeDetector;
    private readonly IServerProcessManager _serverProcessManager;
    private readonly ILogger<TransportCreationService> _logger;
    private readonly bool _verboseLogging;

    public TransportCreationService(
        IServerTypeDetectionService serverTypeDetector,
        IServerProcessManager serverProcessManager,
        ILogger<TransportCreationService> logger,
        EvaluationCommandOptions? commandOptions = null)
    {
        _serverTypeDetector = serverTypeDetector;
        _serverProcessManager = serverProcessManager;
        _logger = logger;
        _verboseLogging = commandOptions?.Verbose ?? false;
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

        // Use server process manager to get or start the server process
        // This will reuse existing processes instead of creating new ones
        await _serverProcessManager.GetOrStartServerProcessAsync(serverConfig, cancellationToken);

        if (_verboseLogging)
        {
            var action = string.IsNullOrEmpty(serverConfig.Path) ? "Connecting to existing" : "Using managed";
            _logger.LogInformation("{Action} HTTP server at: {Url}", action, serverConfig.Url);
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "HttpServer",
            Endpoint = serverUri
        });
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