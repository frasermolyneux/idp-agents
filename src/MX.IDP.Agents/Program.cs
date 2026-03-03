using Azure.Identity;
using Azure.ResourceManager;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Microsoft.SemanticKernel;

using MX.IDP.Agents.Services;
using MX.IDP.Agents.Tools;

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

var embeddingDeployment = builder.Configuration["AzureOpenAI:EmbeddingDeployment"]
                          ?? builder.Configuration["AzureOpenAI__EmbeddingDeployment"]
                          ?? "text-embedding-ada-002";

// Azure Resource Manager client for tools
var credential = new DefaultAzureCredential();
builder.Services.AddSingleton(new ArmClient(credential));

// Register Semantic Kernel with Azure tools as plugins
builder.Services.AddKernel();
builder.Services.AddAzureOpenAIChatCompletion(
    deploymentName: chatDeployment,
    endpoint: azureOpenAIEndpoint,
    credentials: credential);

#pragma warning disable SKEXP0010
builder.Services.AddAzureOpenAITextEmbeddingGeneration(
    deploymentName: embeddingDeployment,
    endpoint: azureOpenAIEndpoint,
    credential: credential);
#pragma warning restore SKEXP0010

// Azure AI Search clients
var searchEndpoint = builder.Configuration["AzureSearch:Endpoint"]
                     ?? builder.Configuration["AzureSearch__Endpoint"]
                     ?? "";
if (!string.IsNullOrEmpty(searchEndpoint))
{
    builder.Services.AddSingleton(new SearchIndexClient(new Uri(searchEndpoint), credential));
    builder.Services.AddSingleton(new SearchClient(new Uri(searchEndpoint), "knowledge-index", credential));
}

// Register tool classes for DI
builder.Services.AddSingleton<SubscriptionTool>();
builder.Services.AddSingleton<ResourceGraphTool>();
builder.Services.AddSingleton<AdvisorTool>();
builder.Services.AddSingleton<PolicyTool>();
builder.Services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
builder.Services.AddSingleton<GitHubTool>();
builder.Services.AddSingleton<KnowledgeTool>();
builder.Services.AddSingleton<IKnowledgeIndexService, KnowledgeIndexService>();

// Register agent router and IDP chat service
builder.Services.AddScoped<IAgentRouter, AgentRouter>();
builder.Services.AddScoped<IIdpChatService, ChatCompletionService>();

// JWT Bearer auth with Microsoft Identity Web
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Build().Run();
