# Semantic Kernel Function Name Mismatch Reproduction

This repository contains a sample application that demonstrates a function name mismatch issue when using Semantic Kernel with Model Context Protocol (MCP) tools.

## Issue Description

When integrating external tools (such as Model Context Protocol tools) with Semantic Kernel, there's a mismatch between how functions are registered in the kernel and how they're expected to be called by the AI model. Functions are registered with a plugin name prefix (e.g., `PizzaTools_get_pizzas`), but the model appears to attempt to call them by their original names (e.g., `get_pizzas`).

### Expected Behavior

The agent should be able to call functions that have been registered in the kernel, regardless of whether they have a plugin name prefix or not.

### Actual Behavior

The agent appears unable to correctly match and invoke the functions when they have a plugin name prefix. Direct invocation works when using the full prefixed name, but the agent appears to attempt to call functions by their original names.

## Reproduction Steps

1. Register MCP tools as kernel functions
2. Add these functions to a plugin with a name prefix
3. Direct function invocation works when explicitly using the prefixed name
4. When using the agent, function calls fail as the model appears to attempt to call the non-prefixed version

## Code Example

See `Program.cs` for a complete working example. Key sections include:

```csharp
// Converting MCP tools to Kernel functions
var kernelFunctions = new List<KernelFunction>();
foreach (var tool in tools)
{
    try {
        var function = tool.AsKernelFunction();
        kernelFunctions.Add(function);
        Console.WriteLine($"Successfully registered tool: {tool.Name} as function: {function.Name}");
    }
    catch (Exception ex) {
        Console.WriteLine($"Error registering tool {tool.Name}: {ex.Message}");
    }
}

// Adding functions to the kernel with a plugin name prefix
Kernel kernel = builder.Build();
kernel.Plugins.AddFromFunctions("PizzaTools", kernelFunctions);

// Direct invocation works with the full prefixed name
var getPizzasTool = kernel.Plugins["PizzaTools"]["get_pizzas"];
var pizzaResult = await kernel.InvokeAsync(getPizzasTool);

// But agent invocation fails as it tries to call the non-prefixed name
ChatCompletionAgent agent = new()
{
    Instructions = "...",
    Name = "PizzaAgent",
    Kernel = kernel
};
```

## Impact

This issue makes it challenging to integrate external tools with Semantic Kernel, as it requires additional workarounds to ensure the model knows the correct function names to call. It affects the usability and reliability of tool integration in agent-based applications.

## Environment

- .NET 9.0
- Semantic Kernel 1.53.0
- Microsoft.SemanticKernel.Agents.Core 1.53.0
- ModelContextProtocol 0.2.0-preview.1
- Azure OpenAI Service (gpt-4o)

## Setup Instructions

1. Clone the repository
2. Set your Azure OpenAI API key in `appsettings.json` or using user secrets:
   ```
   dotnet user-secrets set "OpenAI:ApiKey" "your-api-key"
   ```
3. Run the application:
   ```
   dotnet run
   ```

## Project Structure

- `Program.cs` - Main application code that demonstrates the issue
- `PizzaOrderClient.csproj` - Project file with required dependencies
- `appsettings.json` - Configuration file for OpenAI and Pizza MCP endpoints
- `openapi.yaml` - OpenAPI specification for the Pizza API

## License

MIT
