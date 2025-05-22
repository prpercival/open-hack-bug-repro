using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;
using System.IO;

namespace PizzaOrderClient;

class Program
{
    private static IConfigurationRoot Configuration = null!;
    private static string userId = string.Empty;
    private static string apiKey = string.Empty;
    private static string modelId = string.Empty;
    private static string endpoint = string.Empty;

    static async Task Main(string[] args)
    {
        // Configure configuration with appsettings.json, user secrets, and environment variables
        Configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();        // Connection details for Azure OpenAI

        userId = Configuration["UserId"] ?? string.Empty;

        apiKey = Configuration["OpenAI:ApiKey"] ?? string.Empty;
        modelId = Configuration["OpenAI:ChatModelId"] ?? "gpt-4o";
        endpoint = Configuration["OpenAI:Endpoint"] ?? "https://openhackpizza.cognitiveservices.azure.com/";

        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("Please provide a valid OpenAI:ApiKey in appsettings.json, user secrets, or environment variables.");
            return;
        }        // Create an MCPClient for the Pizza MCP server
        string mcpEndpoint = Configuration["PizzaMCP:Endpoint"] ?? "https://ca-pizza-mcp-vqqlxwmln5lf4.proudglacier-687aa477.eastus2.azurecontainerapps.io/mcp";
        Console.WriteLine($"Connecting to Pizza MCP server at: {mcpEndpoint}");

        await using var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(
            new SseClientTransportOptions
            {
                Name = "PizzaMCPClient",
                Endpoint = new Uri(mcpEndpoint),
                UseStreamableHttp = true
            }
        ));

        //var test = mcpClient.ListResourcesAsync().ConfigureAwait(false);

        // Retrieve the list of tools available on the Pizza MCP server
        var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
        Console.WriteLine("\nAvailable Pizza MCP tools:");
        foreach (var tool in tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description}");
        }

#pragma warning disable CS0612, CS0618, SKEXP0001
        // Prepare and build kernel with the MCP tools as Kernel functions
        var builder = Kernel.CreateBuilder();
        builder.Services
            .AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Trace))
            .AddAzureOpenAIChatCompletion(
                endpoint: endpoint,
                deploymentName: modelId,
                apiKey: apiKey);

        // Convert MCP tools to Kernel functions with proper error handling
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

        Kernel kernel = builder.Build();
        kernel.Plugins.AddFromFunctions("PizzaTools", kernelFunctions);

        // Print out the actual registered function names for debugging
        Console.WriteLine("\nRegistered kernel functions:");
        foreach (var plugin in kernel.Plugins)
        {
            Console.WriteLine($"Plugin: {plugin.Name}");
            foreach (var function in plugin.ToList())
            {
                Console.WriteLine($"  - {function.Name}");
            }
        }

        // Let's try to directly invoke a tool to verify it works
        Console.WriteLine("\nAttempting to directly invoke PizzaTools_get_pizzas tool...");
        try
        {
            // Try to find the function by its base name
            var functionName = "get_pizzas";
            var pluginFunctions = kernel.Plugins["PizzaTools"];
            var getPizzasTool = pluginFunctions.FirstOrDefault(f => f.Name.EndsWith(functionName));

            if (getPizzasTool == null)
            {
                Console.WriteLine($"Function ending with '{functionName}' not found. Using default lookup.");
                getPizzasTool = kernel.Plugins["PizzaTools"]["get_pizzas"];
            }

            var pizzaResult = await kernel.InvokeAsync(getPizzasTool);
            Console.WriteLine($"Direct tool invocation result of {getPizzasTool.Name} function:\n{pizzaResult}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error directly invoking tool: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
        }

        // Configure execution settings with tool calling behavior
        OpenAIPromptExecutionSettings executionSettings = new()
        {
            Temperature = 0,
            //ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
        };
#pragma warning restore CS0612, CS0618, SKEXP0001

        Console.WriteLine("\nRegistered kernel functions:");
        foreach (var plugin in kernel.Plugins)
        {
            Console.WriteLine($"Plugin: {plugin.Name}");
            foreach (var function in plugin.ToList())
            {
                Console.WriteLine($"  - {function.Name}");
            }
        }

        // Test using Pizza tools with clearer prompting
        var prompt = "Use the PizzaTools_get_pizzas tool to list all available menu items";
        Console.WriteLine($"Testing with explicit prompt: {prompt}");
        try
        {
            var result = await kernel.InvokePromptAsync(prompt, new(executionSettings));
            Console.WriteLine($"\nPrompt test result:\n{result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during prompt test: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
        }

        // Define the agent
        ChatCompletionAgent agent = new()
        {
            Instructions = @"You are a helpful pizza ordering assistant.
You can help users browse the menu, view pizzas and toppings, place orders, and track existing orders.
When placing orders, you need a userId (you can use '088e3e04-e29d-41b4-b18d-012b0f13a6ef' for testing).
Be conversational and helpful. If users have questions about pizzas or the ordering process, assist them using the available tools.
Format any JSON responses in a user-friendly way.",
            Name = "PizzaAgent",
            Kernel = kernel,
#pragma warning disable CS0612, CS0618, SKEXP0001
            Arguments = new KernelArguments(new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true }) })//new KernelArguments(executionSettings),
#pragma warning restore CS0612, CS0618, SKEXP0001

        };

        // Start interactive chat loop
        Console.WriteLine("\n\n=== Pizza Ordering Assistant ===");
        Console.WriteLine("Type your questions or requests about pizzas and ordering. Type 'exit' to quit.");

        while (true)
        {
            Console.Write("\nYou: ");
            var userInput = Console.ReadLine();

            if (string.IsNullOrEmpty(userInput))
                continue;

            if (userInput.ToLower() == "exit")
            {
                Console.WriteLine("Thank you for using the Pizza Ordering Assistant!");
                break;
            }

            Console.WriteLine("\nPizzaAgent is thinking...");
            try
            {
                // Get response from the agent
                ChatMessageContent response = await agent.InvokeAsync(userInput).FirstAsync();
                Console.WriteLine($"\nPizzaAgent: {response.Content}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }
        }
    }
}
