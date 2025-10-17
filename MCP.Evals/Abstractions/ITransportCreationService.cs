using MCP.Evals.Models;
using ModelContextProtocol.Client;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for transport creation service
/// </summary>
public interface ITransportCreationService
{
    /// <summary>
    /// Creates a client transport for the specified type and configuration
    /// </summary>
    Task<IClientTransport> CreateTransportAsync(
        string transportType,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);
}