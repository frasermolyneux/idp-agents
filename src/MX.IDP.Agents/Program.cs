using Azure.Identity;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureFunctionsWebApplication();

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

// Azure OpenAI configuration
var azureOpenAIEndpoint = builder.Configuration["AzureOpenAI:Endpoint"]
                          ?? builder.Configuration["AzureOpenAI__Endpoint"]
                          ?? throw new InvalidOperationException("AzureOpenAI endpoint is not configured.");

var chatDeployment = builder.Configuration["AzureOpenAI:ChatDeployment"]
                     ?? builder.Configuration["AzureOpenAI__ChatDeployment"]
                     ?? "gpt-4.1-mini";

// Register Semantic Kernel
builder.Services.AddKernel();
builder.Services.AddAzureOpenAIChatCompletion(
    deploymentName: chatDeployment,
    endpoint: azureOpenAIEndpoint,
    credentials: new DefaultAzureCredential());

// Register IDP chat service
builder.Services.AddScoped<IIdpChatService, ChatCompletionService>();

// JWT Bearer auth with Microsoft Identity Web
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Build().Run();
