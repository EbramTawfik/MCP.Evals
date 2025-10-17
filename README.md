# MCP.Evals

[![NuGet Version](https://img.shields.io/nuget/v/MCP.Evals)](https://www.nuget.org/packages/MCP.Evals)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/EbramTawfik/MCP.Evals)](https://github.com/EbramTawfik/MCP.Evals/blob/main/LICENSE)

An evaluation framework for testing [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) servers with language models. MCP.Evals provides automated testing capabilities to validate MCP server implementations across different scenarios and ensure reliable tool execution.

## Features

- 🔍 **Automated MCP Server Testing** - Run evaluations against MCP servers
- 🤖 **Multi-Language Model Support** - Compatible with OpenAI, Azure OpenAI, and Anthropic models
- 📊 **Flexible Configuration** - YAML configuration support
- 🚀 **Multiple Transport Methods** - Support for stdio and HTTP transports

- ✅ **Validation Framework** - Configuration and request validation
- 🛠️ **CLI Tool** - Easy-to-use command-line interface

## Installation

### As a Global Tool (Recommended)

```bash
dotnet tool install -g MCP.Evals
```

### As a NuGet Package

```bash
dotnet add package MCP.Evals
```

## Quick Start

### 1. Create an Evaluation Configuration

Create a YAML configuration file (e.g., `my-server-eval.yaml`):

```yaml
# Language model configuration
model:
  provider: openai  # or azure-openai, anthropic
  name: gpt-4o

# MCP server configuration
server:
  transport: stdio
  path: "./my-mcp-server.exe"

# Evaluation test cases
evals:
  - name: basic_math_test
    description: Test basic math operations
    prompt: "Use the calculator tool to add 5 + 3"
    expectedResult: "Should return 8"
    
  - name: echo_test
    description: Test echo functionality
    prompt: "Use the echo tool to repeat 'Hello World'"
    expectedResult: "Should echo back 'Hello World'"
```

### 2. Run Evaluations

```bash
# Run evaluations with API key
McpEval evaluate my-server-eval.yaml --api-key "your-api-key"

# Azure OpenAI with endpoint
McpEval evaluate my-server-eval.yaml --api-key "your-key" --endpoint "https://your-resource.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2025-01-01-preview"

# Save results to file
McpEval evaluate my-server-eval.yaml --output results.json --api-key "your-key"

# Validate configuration without running
McpEval validate my-server-eval.yaml
```

### 3. View Results

Results include detailed execution logs, scoring, and performance metrics:

```json
{
  "configurationName": "my-server-eval",
  "totalEvaluations": 2,
  "successfulEvaluations": 2,
  "failedEvaluations": 0,
  "averageScore": 0.95,
  "evaluations": [
    {
      "name": "basic_math_test",
      "success": true,
      "score": 0.9,
      "executionTimeMs": 1234,
      "toolsUsed": ["calculator"]
    }
  ]
}
```

## Configuration Options

### Language Model Providers

#### OpenAI
```yaml
model:
  provider: openai
  name: gpt-4o
```

#### Azure OpenAI
```yaml
model:
  provider: azure-openai
  name: gpt-4o
```

#### Anthropic
```yaml
model:
  provider: anthropic
  name: claude-3-5-sonnet-20241022
```

### Transport Types

#### Standard I/O (stdio)
```yaml
server:
  transport: stdio
  path: "./my-server.exe"
  args: ["arg1", "arg2"]  # Optional command line arguments
```

#### HTTP
```yaml
server:
  transport: http
  url: "http://localhost:3000/mcp"
  path: "./server.js"  # Optional: auto-start server if not running
```

## Examples

The repository includes example configurations for different scenarios:

- [C# MCP Server](examples/csharp-server-evals.yaml) - Testing a C# MCP server implementation
- [TypeScript MCP Server](examples/typescript-server-evals.yaml) - Testing a TypeScript MCP server

## CLI Commands

### evaluate
Run evaluations from a configuration file:
```bash
McpEval evaluate <config-path> [options]

Options:
  --api-key       API key for language model provider
  --endpoint      Endpoint URL (required for Azure OpenAI)
  --output, -o    Output file path for results (JSON format)
  --format, -f    Output format (json, summary, detailed, clean)
  --verbose, -v   Enable verbose logging
```

### validate
Validate configuration without running evaluations:
```bash
McpEval validate <config-path>
```

## Configuration Options

### API Key Configuration
API keys must be provided via command line argument:
- Use `--api-key` option when running commands

### Azure OpenAI Configuration
For Azure OpenAI, you also need to provide the endpoint:
- Use `--endpoint` command line option with your Azure OpenAI deployment URL (e.g., `https://your-resource.openai.azure.com/openai/deployments/model-name/chat/completions?api-version=2025-01-01-preview`)

### Logging Configuration
Control logging verbosity with the `--verbose` command line flag:
- Default: Information level logging
- With `--verbose`: Debug level logging

## Requirements

- .NET 8.0 or later
- Valid API keys for chosen language model provider
- MCP server to evaluate

## Architecture

MCP.Evals follows SOLID principles with a clean architecture:

- **Commands** - CLI command implementations
- **Services** - Core business logic (orchestration, scoring, transport management)
- **Models** - Data transfer objects and configuration models
- **Abstractions** - Interfaces for dependency injection
- **Validation** - FluentValidation rules for configuration validation

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- 📖 [Model Context Protocol Documentation](https://modelcontextprotocol.io/)
- 🐛 [Report Issues](https://github.com/EbramTawfik/MCP.Evals/issues)
- 💬 [Discussions](https://github.com/EbramTawfik/MCP.Evals/discussions)

## Acknowledgments

This project was inspired by [mcp-evals](https://github.com/mclenhard/mcp-evals) by mclenhard.

---

Made with ❤️ for the MCP community