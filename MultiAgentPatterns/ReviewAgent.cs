using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using OpenAI.Chat;
using System.Text.Json;
using static MultiAgentPatterns.GroupChatService;

namespace MultiAgentPatterns
{
    public class ReviewAgent : IAgent
    {
        private readonly OpenAIClientProvider _clientProvider;
        public ReviewAgent(OpenAIClientProvider clientProvider)
        {
            _clientProvider = clientProvider;
        }

        public string Name => nameof(ReviewAgent);

        public string Description => "Reviewer of the poem. Push back or Approve the result.";

        public Task<ConversationResult> StartAsync(TaskOrchestrationContext orchestrationContext, ConversationContext conversationContext)
        {
            return orchestrationContext.CallActivityAsync<ConversationResult>(nameof(ReviewAsync), conversationContext);
        }

        [Function(nameof(ReviewAsync))]
        public async Task<ConversationResult> ReviewAsync([ActivityTrigger] ConversationContext conversationContext, FunctionContext executionContext)
        {
            var conversationResult = new ConversationResult();

            // Here we call LLM to select an agent with Structured Output
            var chatClient = _clientProvider.GetChatClient();
            var builder = new ChatCompletionWithToolsBuilder(chatClient);
            builder.AddChatMessages(conversationContext.History.Convert()); // Chat Message from user

            var systemPrompt = $"You are a professional reviewer. Check if it satisfy the original user request with professional quality. Decide if you can approve it or not. Provide the reason and instruction for the next step."; // System Prompt

            var userPrompt = $"[{conversationContext.RequestedAgent}] {conversationContext.UserPrompt}";
            builder.AddChatMessage(new SystemChatMessage(systemPrompt));
            builder.AddChatMessage(new UserChatMessage(userPrompt));

            var newHistory = new List<History>();
            newHistory.Add(new History(userPrompt, MessageType.User));

            // Structured Output
            var options = new ChatResponseFormatOptions()
            {
                JsonSchemaFormatName = "ReviewAgent",
                JsonSchema = GetStructuredOutputSchema()
            };
            builder.SetResponseFormat(options);

            var result = await builder.ExecuteAsync();
            JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var reviewResult = JsonSerializer.Deserialize<ReviewResult>(result.Result, jsonSerializerOptions);
            var assistantMessage = $"[{Name}] {result.Result}";
            newHistory.Add(new History(assistantMessage, MessageType.Assistant));
            conversationResult.Text = reviewResult.Reason;
            conversationResult.Approved = reviewResult.Approve;
            conversationResult.NewHistory = newHistory;
            return conversationResult;
        }

        private BinaryData GetStructuredOutputSchema()
        {
            return BinaryData.FromObjectAsJson(
                new
                {
                    Type = "object",
                    Properties = new
                    {
                        Approve = new
                        {
                            Type = "boolean",
                            Description = "Approve the poem or not."
                        },
                        Reason = new
                        {
                            Type = "string",
                            Description = "Explain the result is chosen."
                        }
                    },
                    Required = new string[] { "Approve", "Reason" },
                    AdditionalProperties = false
                }, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );
        }

        public record class ReviewResult
        {
            public bool Approve { get; set; }
            public string Reason { get; set; }
        }
    }
}
