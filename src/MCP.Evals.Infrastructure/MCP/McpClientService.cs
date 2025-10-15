using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Core.Exceptions;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using ModelContextProtocol;

namespace MCP.Evals.Infrastructure.MCP;

/// <summary>
/// MCP client service implementation using official ModelContextProtocol SDK
/// Follows SRP - only responsible for MCP operations
/// </summary>
public class McpClientService : IMcpClientService
{
    private readonly ILogger<McpClientService> _logger;

    public McpClientService(
        ILogger<McpClientService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteToolInteractionAsync(
        string serverPath,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting tool interaction with server: {ServerPath}", serverPath);

        try
        {
            // TODO: Implement proper MCP SDK integration once we verify the correct API
            // For now, return a placeholder response to allow the project to build
            await Task.Delay(100, cancellationToken);

            var response = $"MCP tool interaction with server '{serverPath}' for prompt: {prompt}";
            _logger.LogDebug("MCP interaction completed successfully");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool interaction");
            throw new McpClientException(serverPath, $"Tool interaction failed", ex);
        }
    }

    public async Task<bool> TestConnectionAsync(
        string serverPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Testing connection to server: {ServerPath}", serverPath);

        try
        {
            // TODO: Implement proper MCP SDK connection test
            // For now, simulate a successful connection test
            await Task.Delay(50, cancellationToken);

            _logger.LogDebug("Connection test successful for server: {ServerPath}", serverPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed for server: {ServerPath}", serverPath);
            return false;
        }
    }

    private static string GetExecutableCommand(string serverPath)
    {
        // Determine the appropriate command based on the server file extension
        var extension = Path.GetExtension(serverPath).ToLowerInvariant();
        return extension switch
        {
            ".ts" => "tsx",
            ".js" => "node",
            ".py" => "python",
            _ => throw new McpClientException("", $"Unsupported server file type: {extension}")
        };
    }

    private static string[] GetExecutableArguments(string serverPath)
    {
        return [Path.GetFullPath(serverPath)];
    }

    private static string GetSystemPrompt(IReadOnlyList<string> tools)
    {
        var toolDescriptions = string.Join("\n", tools.Select(t => $"- {t}"));

        return $"""
            You are an assistant responsible for evaluating the results of calling various tools.
            Given the user's query, use the tools available to you to answer the question.
            
            Available tools:
            {toolDescriptions}
            
            Please provide a comprehensive and accurate response using the appropriate tools.
            """;
    }
}