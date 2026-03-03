using System.Reflection;

using Microsoft.SemanticKernel;

using MX.IDP.Agents.Tools;

using Xunit;

namespace MX.IDP.Agents.Tests;

public class ToolRegistrationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void SubscriptionTool_HasKernelFunction_ListSubscriptions()
    {
        var method = typeof(SubscriptionTool).GetMethod("ListSubscriptionsAsync");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("list_subscriptions", attr!.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResourceGraphTool_HasKernelFunction_QueryResources()
    {
        var method = typeof(ResourceGraphTool).GetMethod("QueryResourcesAsync");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("query_resources", attr!.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResourceGraphTool_QueryResources_HasRequiredQueryParameter()
    {
        var method = typeof(ResourceGraphTool).GetMethod("QueryResourcesAsync");
        var parameters = method!.GetParameters();

        Assert.True(parameters.Length >= 1);
        Assert.Equal("query", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResourceGraphTool_QueryResources_HasOptionalSubscriptionIds()
    {
        var method = typeof(ResourceGraphTool).GetMethod("QueryResourcesAsync");
        var parameters = method!.GetParameters();

        var subIdParam = parameters.FirstOrDefault(p => p.Name == "subscriptionIds");
        Assert.NotNull(subIdParam);
        Assert.True(subIdParam!.HasDefaultValue);
        Assert.Null(subIdParam.DefaultValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AdvisorTool_HasKernelFunction_GetRecommendations()
    {
        var method = typeof(AdvisorTool).GetMethod("GetRecommendationsAsync");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("get_advisor_recommendations", attr!.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AdvisorTool_HasOptionalCategoryAndImpactParameters()
    {
        var method = typeof(AdvisorTool).GetMethod("GetRecommendationsAsync");
        var parameters = method!.GetParameters();

        var categoryParam = parameters.FirstOrDefault(p => p.Name == "category");
        Assert.NotNull(categoryParam);
        Assert.True(categoryParam!.HasDefaultValue);

        var impactParam = parameters.FirstOrDefault(p => p.Name == "impact");
        Assert.NotNull(impactParam);
        Assert.True(impactParam!.HasDefaultValue);

        var maxParam = parameters.FirstOrDefault(p => p.Name == "maxResults");
        Assert.NotNull(maxParam);
        Assert.True(maxParam!.HasDefaultValue);
        Assert.Equal(25, maxParam.DefaultValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PolicyTool_HasKernelFunction_GetPolicyCompliance()
    {
        var method = typeof(PolicyTool).GetMethod("GetPolicyComplianceAsync");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("get_policy_compliance", attr!.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PolicyTool_HasKernelFunction_GetNonCompliantResources()
    {
        var method = typeof(PolicyTool).GetMethod("GetNonCompliantResourcesAsync");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("get_non_compliant_resources", attr!.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AllTools_CanBeRegisteredAsKernelPlugins()
    {
        var kernel = Kernel.CreateBuilder().Build();
        var armClient = new Azure.ResourceManager.ArmClient(new Azure.Identity.DefaultAzureCredential());

        var subscriptionTool = new SubscriptionTool(armClient);
        var resourceGraphTool = new ResourceGraphTool(armClient);
        var advisorTool = new AdvisorTool(armClient);
        var policyTool = new PolicyTool(armClient);
        var gitHubTool = new GitHubTool(Moq.Mock.Of<MX.IDP.Agents.Services.IGitHubClientFactory>());
        var knowledgeTool = new KnowledgeTool(Moq.Mock.Of<MX.IDP.Agents.Services.IKnowledgeIndexService>());
        var campaignTool = new CampaignTool(Moq.Mock.Of<MX.IDP.Agents.Services.ICampaignService>(), Moq.Mock.Of<MX.IDP.Agents.Services.ICampaignOrchestrationService>());

        // These should not throw — verifies SK function metadata is valid
        kernel.Plugins.AddFromObject(subscriptionTool, "AzureSubscriptions");
        kernel.Plugins.AddFromObject(resourceGraphTool, "AzureResourceGraph");
        kernel.Plugins.AddFromObject(advisorTool, "AzureAdvisor");
        kernel.Plugins.AddFromObject(policyTool, "AzurePolicy");
        kernel.Plugins.AddFromObject(gitHubTool, "GitHub");
        kernel.Plugins.AddFromObject(knowledgeTool, "Knowledge");
        kernel.Plugins.AddFromObject(campaignTool, "Campaign");

        Assert.Equal(7, kernel.Plugins.Count);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("CreateIssueAsync", "create_issue")]
    [InlineData("ListIssuesAsync", "list_issues")]
    [InlineData("ListRepositoriesAsync", "list_repositories")]
    [InlineData("GetActionsStatusAsync", "get_actions_status")]
    [InlineData("AssignIssueAsync", "assign_issue")]
    public void GitHubTool_HasKernelFunctions(string methodName, string functionName)
    {
        var method = typeof(GitHubTool).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(functionName, attr!.Name);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("SearchKnowledgeBaseAsync", "search_knowledge_base")]
    [InlineData("ListKnowledgeSourcesAsync", "list_knowledge_sources")]
    [InlineData("TriggerReindexAsync", "trigger_reindex")]
    public void KnowledgeTool_HasKernelFunctions(string methodName, string functionName)
    {
        var method = typeof(KnowledgeTool).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(functionName, attr!.Name);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("CreateCampaignAsync", "create_campaign")]
    [InlineData("ListCampaignsAsync", "list_campaigns")]
    [InlineData("RunCampaignAsync", "run_campaign")]
    [InlineData("GetCampaignFindingsAsync", "get_campaign_findings")]
    [InlineData("ListCampaignTemplatesAsync", "list_campaign_templates")]
    [InlineData("CreateCampaignFromTemplateAsync", "create_campaign_from_template")]
    public void CampaignTool_HasKernelFunctions(string methodName, string functionName)
    {
        var method = typeof(CampaignTool).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<KernelFunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(functionName, attr!.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AllTools_HaveTelemetryClientOptionalParameter()
    {
        // Verify all tools accept optional TelemetryClient
        var toolTypes = new[] { typeof(SubscriptionTool), typeof(ResourceGraphTool), typeof(AdvisorTool), typeof(PolicyTool) };

        foreach (var type in toolTypes)
        {
            var ctor = type.GetConstructors().First();
            var telemetryParam = ctor.GetParameters().FirstOrDefault(p => p.Name == "telemetryClient");
            Assert.NotNull(telemetryParam);
            Assert.True(telemetryParam!.HasDefaultValue, $"{type.Name} should have optional TelemetryClient");
        }
    }
}
