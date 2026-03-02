using System.Text.Json;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace MX.IDP.Agents.Functions;

/// <summary>
/// Durable orchestration for human-in-the-loop confirmation of destructive actions.
/// The agent identifies actions requiring confirmation, starts this orchestration,
/// and waits for user approval before executing.
/// </summary>
public static class ConfirmationOrchestration
{
    public const string ApprovalEventName = "UserApproval";

    [Function("ConfirmationOrchestrator")]
    public static async Task<ConfirmationResult> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ConfirmationRequest>()!;
        var logger = context.CreateReplaySafeLogger("ConfirmationOrchestrator");

        logger.LogInformation("Awaiting user confirmation for action: {Action}", input.ActionDescription);

        // Wait for user approval (timeout after 5 minutes)
        using var cts = new CancellationTokenSource();
        var approvalTask = context.WaitForExternalEvent<bool>(ApprovalEventName, cts.Token);
        var timeoutTask = context.CreateTimer(TimeSpan.FromMinutes(5), cts.Token);

        var winner = await Task.WhenAny(approvalTask, timeoutTask);

        if (winner == approvalTask)
        {
            var approved = approvalTask.Result;
            if (approved)
            {
                logger.LogInformation("User approved action: {Action}", input.ActionDescription);
                return new ConfirmationResult
                {
                    Status = "approved",
                    Message = $"Action approved: {input.ActionDescription}"
                };
            }
            else
            {
                logger.LogInformation("User rejected action: {Action}", input.ActionDescription);
                return new ConfirmationResult
                {
                    Status = "rejected",
                    Message = $"Action rejected: {input.ActionDescription}"
                };
            }
        }
        else
        {
            logger.LogWarning("Confirmation timed out for action: {Action}", input.ActionDescription);
            return new ConfirmationResult
            {
                Status = "timed_out",
                Message = $"Confirmation timed out for: {input.ActionDescription}"
            };
        }
    }

    [Function("StartConfirmation")]
    public static async Task<HttpResponseData> StartConfirmation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "confirm/start")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        var logger = context.GetLogger<object>();

        var request = await JsonSerializer.DeserializeAsync<ConfirmationRequest>(req.Body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request is null)
        {
            var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Invalid request body.");
            return badResponse;
        }

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync("ConfirmationOrchestrator", request);

        logger.LogInformation("Started confirmation orchestration {InstanceId} for: {Action}", instanceId, request.ActionDescription);

        var response = req.CreateResponse(System.Net.HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new
        {
            instanceId,
            statusUrl = $"/api/confirm/{instanceId}/status",
            approveUrl = $"/api/confirm/{instanceId}/approve",
            rejectUrl = $"/api/confirm/{instanceId}/reject"
        });
        return response;
    }

    [Function("ApproveConfirmation")]
    public static async Task<HttpResponseData> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "confirm/{instanceId}/approve")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId)
    {
        await client.RaiseEventAsync(instanceId, ApprovalEventName, true);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "approved", instanceId });
        return response;
    }

    [Function("RejectConfirmation")]
    public static async Task<HttpResponseData> Reject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "confirm/{instanceId}/reject")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId)
    {
        await client.RaiseEventAsync(instanceId, ApprovalEventName, false);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "rejected", instanceId });
        return response;
    }

    [Function("GetConfirmationStatus")]
    public static async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "confirm/{instanceId}/status")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId)
    {
        var metadata = await client.GetInstanceAsync(instanceId);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        if (metadata is null)
        {
            await response.WriteAsJsonAsync(new { status = "not_found" });
        }
        else
        {
            await response.WriteAsJsonAsync(new
            {
                instanceId = metadata.InstanceId,
                status = metadata.RuntimeStatus.ToString(),
                createdAt = metadata.CreatedAt,
                lastUpdatedAt = metadata.LastUpdatedAt
            });
        }
        return response;
    }
}

public class ConfirmationRequest
{
    public string ActionDescription { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string? ActionType { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
}

public class ConfirmationResult
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
