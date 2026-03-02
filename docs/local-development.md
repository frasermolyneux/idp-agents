# Local Development - IDP Agents

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (for local Azure Storage emulation)

## Authentication Setup

The function app uses JWT Bearer auth with Entra ID. Configure via `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureAd__Instance": "https://login.microsoftonline.com/",
    "AzureAd__TenantId": "<your-tenant-id>",
    "AzureAd__ClientId": "<idp-agents-client-id>",
    "AzureAd__Audience": "api://<tenant-id>/idp-agents-dev",
    "AzureOpenAI__Endpoint": "https://<ai-services-name>.cognitiveservices.azure.com/",
    "AzureOpenAI__ChatDeployment": "gpt-4.1-mini"
  }
}
```

> **Note**: For Azure OpenAI access, ensure your Azure AD user has "Cognitive Services OpenAI User" role on the AI Services resource. The app uses `DefaultAzureCredential`.

## Azure CLI Login

```bash
az login
az account set --subscription "<dev-subscription-id>"
```

## Starting Azurite

Durable Functions requires Azure Storage. Start Azurite before running:

```bash
# If installed globally
azurite --silent

# Or via Docker
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

## Running

### Command Line

```bash
cd src/MX.IDP.Agents
func start
```

The function app starts on `http://localhost:7071`.

### Available Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/chat` | POST | Send a chat message |
| `/api/whoami` | GET | Check authentication identity |
| `/api/confirm/start` | POST | Start a confirmation orchestration |
| `/api/confirm/{id}/approve` | POST | Approve a pending confirmation |
| `/api/confirm/{id}/reject` | POST | Reject a pending confirmation |
| `/api/confirm/{id}/status` | GET | Check confirmation status |

## Building & Testing

```bash
cd src

# Build
dotnet build MX.IDP.Agents/MX.IDP.Agents.csproj

# Run tests
dotnet test MX.IDP.Agents.Tests/MX.IDP.Agents.Tests.csproj
```

## Project Structure

```
src/
├── MX.IDP.Agents/                     # Main function app
│   ├── Functions/
│   │   ├── ChatFunction.cs            # Chat endpoint
│   │   ├── ConfirmationOrchestration.cs # Human-in-the-loop pattern
│   │   ├── HealthFunction.cs          # Health check
│   │   └── WhoAmIFunction.cs          # Auth debug endpoint
│   ├── Models/                        # ChatRequest, ChatResponse, TokenUsage
│   ├── Services/                      # ChatCompletionService (SK integration)
│   └── Program.cs                     # DI, SK kernel, auth setup
├── MX.IDP.Agents.ServiceDefaults/     # Shared service configuration
└── MX.IDP.Agents.Tests/              # Unit tests (xUnit + Moq)
```

## Troubleshooting

### Azure OpenAI 403/401
Ensure your Azure AD user has "Cognitive Services OpenAI User" role on the AI Services resource. Use `az role assignment create` if needed.

### Storage connection errors
Start Azurite before running the function app. Durable Functions requires storage for orchestration state.

### Token validation errors
Check that `AzureAd__ClientId` matches the idp-agents app registration and `AzureAd__Audience` matches the identifier URI.
