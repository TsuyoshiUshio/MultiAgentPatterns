using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using OpenAI.Chat;

namespace MultiAgentPatterns
{
    public class PoetAgent : IAgent
    {
        private readonly OpenAIClientProvider _clientProvider;
        public PoetAgent(OpenAIClientProvider openAIClientProvider)
        {
            _clientProvider = openAIClientProvider;
        }

        public string Name => nameof(PoetAgent);

        public string Description => "A Professional poet";

        public Task<ConversationResult> StartAsync(TaskOrchestrationContext orchestrationContext, ConversationContext conversationContext)
        {
            return orchestrationContext.CallActivityAsync<ConversationResult>(nameof(WritePoemAsync), conversationContext);
        }

        [Function(nameof(WritePoemAsync))]
        public async Task<ConversationResult> WritePoemAsync([ActivityTrigger] ConversationContext conversationContext, FunctionContext executionContext)
        {
            var conversationResult = new ConversationResult();

            // Here we call LLM to select an agent with Structured Output
            var chatClient = _clientProvider.GetChatClient();
            var builder = new ChatCompletionWithToolsBuilder(chatClient);
            builder.AddChatMessages(conversationContext.History.Convert()); // Chat Message from user

            var systemPrompt = $"You are an professional poem writer who can express the emotion with poetic expression."; // System Prompt

            var userPrompt = $"[{conversationContext.RequestedAgent}] {conversationContext.UserPrompt}";
            builder.AddChatMessage(new SystemChatMessage(systemPrompt));
            builder.AddChatMessage(new UserChatMessage(userPrompt));

            var newHistory = new List<History>();
            newHistory.Add(new History(userPrompt, MessageType.User));

            var result = await builder.ExecuteAsync();
            var assistantMessage = $"[{Name}] {result.Result}";
            newHistory.Add(new History(assistantMessage, MessageType.Assistant));
            conversationResult.Text = assistantMessage;
            conversationResult.NewHistory = newHistory;
            return conversationResult;
        }
    }
}
