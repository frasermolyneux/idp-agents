using System.ComponentModel;
using System.Text.Json;

using Azure.Identity;
using Azure.ResourceManager;

using Microsoft.ApplicationInsights;
using Microsoft.SemanticKernel;

namespace MX.IDP.Agents.Tools;

public class SubscriptionTool
{
    private readonly ArmClient _armClient;
    private readonly TelemetryClient? _telemetryClient;

    public SubscriptionTool(ArmClient armClient, TelemetryClient? telemetryClient = null)
    {
        _armClient = armClient;
        _telemetryClient = telemetryClient;
    }

    [KernelFunction("list_subscriptions")]
    [Description("Lists all Azure subscriptions the platform has access to. Returns subscription name, ID, and state.")]
    public async Task<string> ListSubscriptionsAsync()
    {
        _telemetryClient?.TrackEvent("ToolInvocation", new Dictionary<string, string>
        {
            ["Tool"] = "list_subscriptions"
        });

        var subscriptions = new List<object>();
        await foreach (var sub in _armClient.GetSubscriptions().GetAllAsync())
        {
            subscriptions.Add(new
            {
                name = sub.Data.DisplayName,
                id = sub.Data.SubscriptionId,
                state = sub.Data.State?.ToString() ?? "Unknown"
            });
        }

        return JsonSerializer.Serialize(subscriptions, new JsonSerializerOptions { WriteIndented = true });
    }
}
