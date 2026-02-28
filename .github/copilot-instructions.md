# IDP Agent Platform

## Overview
Azure Functions isolated worker application hosting the IDP agent platform. Uses Durable Task Extension for orchestrations and Microsoft Agent Framework for AI agents.

## Build & Run
```bash
cd src && dotnet build MX.IDP.Agents.sln
dotnet test MX.IDP.Agents.sln
cd MX.IDP.Agents && func start
```

## Project Structure
- `src/MX.IDP.Agents/` — Main Azure Functions application
  - `Agents/` — AI agent definitions (TriageAgent, OpsBot, ComplianceBot, GitHubBot, KnowledgeBot, CampaignBot)
  - `Orchestrations/` — Durable Task orchestrations (ChatOrchestrator, CampaignOrchestrator, ComplianceScan)
  - `Campaigns/` — Campaign service, functions, scheduler, resource-repo mapper
  - `Tools/` — MCP tool implementations (Azure Advisor, ARG, Policy, Cost, GitHub, RAG)
  - `McpServer/` — MCP protocol endpoints and whoami function
  - `Indexer/` — RAG document indexer
  - `Program.cs` — Functions worker startup and DI
- `src/MX.IDP.Agents.Tests/` — Unit tests (xUnit + Moq)
- `terraform/` — App-level Terraform (Function App, slots)

## Key Patterns
- Durable Task Extension for agent orchestrations (fan-out/fan-in, human-in-the-loop)
- MCP tools as HTTP-triggered functions (base ModelContextProtocol SDK, NOT AspNetCore)
- Microsoft.Identity.Web for JWT validation (OBO delegated tokens from idp-web)
- Cosmos DB for campaigns, Azure AI Search for RAG
- Microsoft.Extensions.Logging → Application Insights

## Conventions
- Nullable reference types enabled
- File-scoped namespaces
- 4-space indent, CRLF line endings
- xUnit + Moq for testing, `MethodName_Condition_ExpectedResult` naming
