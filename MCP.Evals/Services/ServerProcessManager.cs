using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Exceptions;
using MCP.Evals.Models;
using System.Diagnostics;
using System.Text;

namespace MCP.Evals.Services;

/// <summary>
/// Service for managing server processes based on server type
/// Follows SRP - only responsible for server process management
/// </summary>
public class ServerProcessManager : IServerProcessManager
{
    private readonly ILogger<ServerProcessManager> _logger;
    private readonly bool _verboseLogging;

    public ServerProcessManager(ILogger<ServerProcessManager> logger)
    {
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    public async Task<Process> StartServerAsync(
        ServerType serverType,
        string serverPath,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateProcessStartInfo(serverType, serverPath, serverConfig);

        if (_verboseLogging)
        {
            _logger.LogInformation("Starting {ServerType} server process: {FileName} {Arguments}",
                serverType, startInfo.FileName, startInfo.Arguments);
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();

        // Give the process a moment to start
        await Task.Delay(1000, cancellationToken);

        if (process.HasExited)
        {
            throw new McpClientException(serverPath,
                $"Server process exited immediately with code: {process.ExitCode}");
        }

        return process;
    }

    public async Task<bool> IsServerReadyAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(2);

        var maxAttempts = 15; // 30 seconds total with 2s intervals
        var attempt = 0;

        while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_verboseLogging && attempt > 0)
                {
                    _logger.LogInformation("Checking server readiness (attempt {Attempt}/{MaxAttempts}): {Endpoint}",
                        attempt + 1, maxAttempts, endpoint);
                }

                // Try to connect to the server with a simple ping
                await httpClient.PostAsync(endpoint,
                    new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}",
                        Encoding.UTF8, "application/json"), cancellationToken);

                if (_verboseLogging)
                {
                    _logger.LogInformation("Server is ready at: {Endpoint}", endpoint);
                }

                return true;
            }
            catch (Exception ex) when (attempt < maxAttempts - 1)
            {
                if (_verboseLogging)
                {
                    _logger.LogDebug(ex, "Server not ready yet: {Error}", ex.Message);
                }

                await Task.Delay(2000, cancellationToken);
                attempt++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to server at {Endpoint}: {Error}", endpoint, ex.Message);
                return false;
            }
        }

        return false;
    }

    private ProcessStartInfo CreateProcessStartInfo(ServerType serverType, string serverPath, ServerConfiguration serverConfig)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(serverPath)) ?? Directory.GetCurrentDirectory()
        };

        var args = serverConfig.Args ?? Array.Empty<string>();

        switch (serverType)
        {
            case ServerType.TypeScriptScript:
                startInfo.FileName = "cmd";
                startInfo.Arguments = $"/c npx tsx \"{Path.GetFullPath(serverPath)}\"";
                if (args.Length > 0)
                {
                    startInfo.Arguments += " " + string.Join(" ", args);
                }
                break;

            case ServerType.NodeScript:
                startInfo.FileName = "node";
                startInfo.Arguments = $"\"{Path.GetFullPath(serverPath)}\"";
                if (args.Length > 0)
                {
                    startInfo.Arguments += " " + string.Join(" ", args);
                }
                break;

            case ServerType.CSharpExecutable:
                startInfo.FileName = Path.GetFullPath(serverPath);
                if (args.Length > 0)
                {
                    startInfo.Arguments = string.Join(" ", args);
                }
                break;

            case ServerType.PythonScript:
                startInfo.FileName = "python";
                startInfo.Arguments = $"\"{Path.GetFullPath(serverPath)}\"";
                if (args.Length > 0)
                {
                    startInfo.Arguments += " " + string.Join(" ", args);
                }
                break;

            default:
                throw new NotSupportedException($"Unsupported server type: {serverType}");
        }

        return startInfo;
    }
}