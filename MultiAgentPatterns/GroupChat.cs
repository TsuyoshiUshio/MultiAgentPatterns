using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using OpenAI.Chat;
using System.Text.Json;

namespace MultiAgentPatterns
{
    public class GroupChat
    {
        private List<IAgent> _agents = new ();
        private readonly AgentRegistry _agentRegistry;
        private readonly ChatClient _chatClient;

        public GroupChat(ChatClient chatClient, AgentRegistry agentRegistry)
        {
            _chatClient = chatClient;
            _agentRegistry = agentRegistry;
        }

        public void RegisterAgent(IAgent agent)
        {
            _agents.Add(agent);
        }

        // Here We write Orchestration Functions
        public async Task<Artifact> StartConversationAsync(TaskOrchestrationContext context, GroupConversationContext groupConversationContext)
        {
            var artifact = new Artifact();
            int count = 0;
            while(count < 20)
            {
                // Read History
                var entityId = new EntityInstanceId(nameof(SessionHistory), groupConversationContext.RequestId);
                var sessionHistoryState = await context.Entities.CallEntityAsync<SessionHistoryState>(entityId, "Get");
                if (sessionHistoryState == null)
                {
                    sessionHistoryState = new SessionHistoryState();
                }
                var history = sessionHistoryState.History;
                var groupConversationUserPrompt = $"[User] {groupConversationContext.UserPrompt}";
                history.Add(new History(groupConversationUserPrompt, MessageType.User));
                artifact.UserPrompt = groupConversationContext.UserPrompt;
                artifact.Conversation.Add(groupConversationUserPrompt);

                var conversationContext = new ConversationContext()
                {
                    GroupConversationUserPrompt = groupConversationContext.UserPrompt,
                    UserPrompt = groupConversationContext.UserPrompt,
                    History = history
                };

                // From the conversation context, decide whith agents to call.
                var selectedAgent = await context.CallActivityAsync<SelectedAgent>(nameof(SelectAgent), conversationContext);
                // Run the agent
                conversationContext.RequestedAgent = "Facilitator";

                ConversationResult result = await DispatchAgent(context, conversationContext, selectedAgent);
                history.AddRange(result.NewHistory);
                sessionHistoryState.History = history;
                // Persist the history
                await context.Entities.CallEntityAsync(entityId, "Set", sessionHistoryState);
                if (result.Approved)
                {
                    // Todo Upload the artifact
                    return artifact;
                } 
                else
                {
                    artifact.Conversation.Add(result.Text);
                    count++;
                }
            }
            return artifact;
        }

        // Here We write Activity Functions
        [Function(nameof(SelectAgent))]
        public async Task<SelectedAgent> SelectAgent([ActivityTrigger] ConversationContext conversationContext, FunctionContext executionContext)
        {
            // Here we call LLM to select an agent with Structured Output
            var builder = new ChatCompletionWithToolsBuilder(_chatClient);
            builder.AddChatMessages(conversationContext.History.Convert()); // Chat Message from user

            builder.AddChatMessage(new UserChatMessage(conversationContext.UserPrompt));
            var systemPrompt = $"You are a facilitator of a group conversation."; // System Prompt
            var userPrompt = $"Look at the user request and history, select the best agent. The conversation won't finish until ReviewAgent approve it."; // User Prompt (Instruction)
            builder.AddChatMessage(new SystemChatMessage(systemPrompt));
            builder.AddChatMessage(new UserChatMessage(userPrompt));
            builder.AddChatTool(GetToolDefinition());

            // Function Calling
            var func = new Func<ChatToolCall, List<ChatMessage>, Task>(async (toolCall, chatMessages) =>
            {
                if (toolCall.FunctionName == "ListAgent")
                {
                    var registeredJson = JsonSerializer.Serialize(_agents);
                    chatMessages.Add(new ToolChatMessage(toolCall.Id, registeredJson));
                }
                await Task.CompletedTask;
            });

            builder.SetFunctionCallSection(func);

            // Structured Output
            var options = new ChatResponseFormatOptions()
            {
                JsonSchemaFormatName = "SelectAgent",
                JsonSchema = GetStructuredOutputSchema()
            };
            builder.SetResponseFormat(options);
            var result = await builder.ExecuteAsync();
            var selectedAgent = JsonSerializer.Deserialize<SelectedAgent>(result.Result);
            return selectedAgent;
        }

        private ChatTool GetToolDefinition()
        {
            return ChatTool.CreateFunctionTool(
                functionName: "ListAgent",
                functionDescription: "List Agent information that is registered");
        }

        private BinaryData GetStructuredOutputSchema()
        {
            return BinaryData.FromObjectAsJson(
                new
                {
                    Type = "object",
                    Properties = new
                    {
                        Request = new
                        {
                            Type = "string",
                            Description = "Request for the agent"
                        },

                        AgentName = new
                        {
                            Type = "string",
                            Description = "Selected Agent AgentName"
                        },
                        Reason = new
                        {
                            Type = "string",
                            Description = "Explain why the agent is chosen."
                        }
                    },
                    Required = new string[] { "Request", "AgentName", "Reason" },
                    AdditionalProperties = false
                }, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );
        }

        public Task<ConversationResult> DispatchAgent(TaskOrchestrationContext context, ConversationContext conversationContext, SelectedAgent selectedAgent)
        {
            // Here we call the agent
            conversationContext.UserPrompt = selectedAgent.Request;
            IAgent agent = _agentRegistry.GetAgent(selectedAgent.AgentName);
            return agent.StartAsync(context, conversationContext);
        }


        public record SelectedAgent(string Request, string AgentName, string Reason);
    }
}
