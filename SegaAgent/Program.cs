/*
 * filename: Program.cs
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SegaAgent.AI.Ollama;
using SegaAgent.AI.Planner;
using SegaAgent.AI.Responder;
using SegaAgent.Agent;
using SegaAgent.Conversation;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<HttpClient>();

builder.Services.AddSingleton<OllamaClient>(sp =>
    ActivatorUtilities.CreateInstance<OllamaClient>(sp));

builder.Services.AddSingleton<AgentPlanner>();

builder.Services.AddSingleton<AgentResponder>();

builder.Services.AddSingleton<ConversationManager>();

builder.Services.AddSingleton<AgentCore>();

builder.Logging.AddConsole();

var host = builder.Build();

var agent = host.Services.GetRequiredService<AgentCore>();

Console.WriteLine();
Console.WriteLine("SEGA Agent Core");
Console.WriteLine("----------------");
Console.WriteLine("Agent initialized.");
Console.WriteLine();
Console.WriteLine("Type 'exit' to close SEGA.");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");

    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Equals(
        "exit",
        StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    try
    {
        var response = await agent.ProcessAsync(input);

        Console.WriteLine();
        Console.WriteLine($"SEGA: {response}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"SEGA error: {ex.Message}");
        Console.WriteLine();
    }
}

Console.WriteLine();
Console.WriteLine("SEGA Agent stopped.");