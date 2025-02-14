using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using OpenAI.Chat;
using System.Text.Json;

namespace MultiAgentPatterns
{
    public class GroupChatService
    {
        private readonly AgentRegistry _agentRegistry;
        private readonly OpenAIClientProvider _clientProvider;

        public GroupChatService(OpenAIClientProvider openAIClientProvider, AgentRegistry agentRegistry)
        {
            _clientProvider = openAIClientProvider;
            _agentRegistry = agentRegistry;
        }

        // Here We write Orchestration Functions
        public async Task<Artifact> StartConversationAsync(TaskOrchestrationContext context, GroupConversationContext groupConversationContext)
        {
            // Read History
            var entityId = new EntityInstanceId(nameof(SessionHistory), groupConversationContext.RequestId);
            var sessionHistoryState = await context.Entities.CallEntityAsync<SessionHistoryState>(entityId, "Get");
            var requestEntityId = new EntityInstanceId(nameof(RequestContext), groupConversationContext.RequestId);
            var requestContextState = await context.Entities.CallEntityAsync<RequestContextState>(requestEntityId, "Get");

            bool newSession = false;
            if (sessionHistoryState == null)
            {
                newSession = true;
                sessionHistoryState = new SessionHistoryState();
            }

            if (requestContextState == null)
            {
                requestContextState = new RequestContextState();
            }

            var artifact = requestContextState.Artifact ?? new Artifact();

            var history = sessionHistoryState.History;
            var groupConversationUserPrompt = $"[User] {groupConversationContext.UserPrompt}";
            if (!newSession)
            {
                // Conversation progress.
                groupConversationUserPrompt = $"[Facilitator] {groupConversationContext.UserPrompt}";
            } 

            history.Add(new History(groupConversationUserPrompt, MessageType.User));
            artifact.Conversation.Add(groupConversationUserPrompt);

            var conversationContext = new ConversationContext()
            {
                GroupConversationUserPrompt = groupConversationContext.UserPrompt,
                UserPrompt = groupConversationContext.UserPrompt,
                History = history,
            };

            // From the conversation context, decide whith agents to call.
            var selectedAgent = await context.CallActivityAsync<SelectedAgent>(nameof(SelectAgent), conversationContext);
            // Run the agent
            conversationContext.RequestedAgent = "Facilitator";
            ConversationResult result = await DispatchAgent(context, conversationContext, selectedAgent);
            history.AddRange(result.NewHistory);
            sessionHistoryState.History = history;
            // Persist the history
            await context.Entities.CallEntityAsync<SessionHistoryState>(entityId, "Add", sessionHistoryState);
            if (result.Approved)
            {
                // Todo Upload the artifact
                // We need to return poem.
                artifact.Conversation.Add(result.Text);
                requestContextState.Artifact = artifact;
                await context.Entities.CallEntityAsync<RequestContextState>(requestEntityId, "Add", requestContextState);
                return artifact;
            }
            else
            {
                artifact.Conversation.Add(result.Text);
                groupConversationContext.UserPrompt = "Progress the next step. Select the next Agent.";
                requestContextState.Artifact = artifact;
                await context.Entities.CallEntityAsync<RequestContextState>(requestEntityId, "Add", requestContextState);
                context.ContinueAsNew(groupConversationContext);
            }

            return artifact;
        }

        // Here We write Activity Functions
        [Function(nameof(SelectAgent))]
        public async Task<SelectedAgent> SelectAgent([ActivityTrigger] ConversationContext conversationContext, FunctionContext executionContext)
        {
            // Here we call LLM to select an agent with Structured Output
            var chatClient = _clientProvider.GetChatClient();
            var builder = new ChatCompletionWithToolsBuilder(chatClient);
            builder.AddChatMessages(conversationContext.History.Convert()); // Chat Message from user

            builder.AddChatMessage(new UserChatMessage(conversationContext.UserPrompt));
            var systemPrompt = $"You are a facilitator of a group conversation. Once a poet wrote poem, then ask editor to edit it. Then ask reviewer for reviewing. If you find push back some of them, pick the best agent to re-do the task."; // System Prompt
            var userPrompt = $"Look at the user request and history, select the best agent. Make sure to call ListAgent for checking the available agent."; // User Prompt (Instruction)
            builder.AddChatMessage(new SystemChatMessage(systemPrompt));
            builder.AddChatMessage(new UserChatMessage(userPrompt));
            builder.AddChatTool(GetToolDefinition());

            // Function Calling
            var func = new Func<ChatToolCall, List<ChatMessage>, Task<List<ChatMessage>>>((toolCall, chatMessages) =>
            {
                if (toolCall.FunctionName == "ListAgent")
                {
                    var registeredJson = JsonSerializer.Serialize(_agentRegistry.Agents);
                    chatMessages.Add(new ToolChatMessage(toolCall.Id, registeredJson));
                    return Task.FromResult(chatMessages);
                }
                return Task.FromResult(new List<ChatMessage>());
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
            JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var selectedAgent = JsonSerializer.Deserialize<SelectedAgent>(result.Result, jsonSerializerOptions);
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
