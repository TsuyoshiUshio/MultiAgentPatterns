using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace MultiAgentPatterns
{
    public class GroupChatOrchstrator
    {
        private readonly OpenAIClientProvider _openAIClientProvider;
        private readonly AgentRegistry _agentRegistry;
        private readonly GroupChatService _groupChatService;
        public GroupChatOrchstrator(OpenAIClientProvider openAIClientProvider, AgentRegistry agentRegistry, GroupChatService groupChatService)
        {
            _openAIClientProvider = openAIClientProvider;
            _agentRegistry = agentRegistry;
            _groupChatService = groupChatService;
        }

        [Function(nameof(GroupChatOrchstrator))]
        public async Task<Artifact> RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            ILogger logger = context.CreateReplaySafeLogger(nameof(GroupChatOrchstrator));

            var groupConversationContext = context.GetInput<GroupConversationContext>();
            // TODO Register Agent for GroupChatService
            // List<string> agents = new List<string>();
            // agents.Add("Facilitator");
            // _groupChatService.RegisterAgents(context, agents); // We are going to use DurableEntity for keeping the state of the registered agents.
            var artifact = await _groupChatService.StartConversationAsync(context, groupConversationContext);

            return artifact;
        }

        [Function("GroupChatOrchstrator_HttpStart")]
        public static async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("GroupChatOrchstrator_HttpStart");

            var conversationContext = await req.ReadFromJsonAsync<GroupConversationContext>();
            // Function input comes from the request content.
            StartOrchestrationOptions startOptions = new StartOrchestrationOptions(InstanceId: conversationContext.RequestId);

            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(GroupChatOrchstrator), conversationContext, startOptions);

            logger.LogInformation("Started orchestration with ID = '{instanceId}'.", conversationContext.RequestId);

            // Returns an HTTP 202 response with an instance management payload.
            // See https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-http-api#start-orchestration
            return await client.CreateCheckStatusResponseAsync(req, conversationContext.RequestId);
        }
    }
}
