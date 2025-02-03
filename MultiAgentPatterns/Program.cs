using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MultiAgentPatterns;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton<OpenAIClientProvider>();
builder.Services.AddSingleton<IAgent, PoetAgent>();
builder.Services.AddSingleton<IAgent, EditorAgent>();
builder.Services.AddSingleton<IAgent, ReviewAgent>();
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<GroupChatService>();

builder.Services.AddOptions<Settings>()
    .Configure<IConfiguration>((settings, configuration) =>
    {
        configuration.GetSection(nameof(Settings))
        .Bind(settings);
    });

builder.Build().Run();
