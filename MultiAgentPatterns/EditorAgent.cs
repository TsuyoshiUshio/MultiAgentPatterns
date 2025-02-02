using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using OpenAI.Chat;

namespace MultiAgentPatterns
{
    public class EditorAgent : IAgent
    {
        private readonly OpenAIClientProvider _clientProvider;
        public EditorAgent(OpenAIClientProvider clientProvider)
        {
            _clientProvider = clientProvider;
        }


        public string Name => nameof(EditorAgent);

        public string Description => "Professional editor";

        public Task<ConversationResult> StartAsync(TaskOrchestrationContext orchestrationContext, ConversationContext conversationContext)
        {
            return orchestrationContext.CallActivityAsync<ConversationResult>(nameof(EditPoemAsync), conversationContext);
        }

        [Function(nameof(EditPoemAsync))]
        public async Task<ConversationResult> EditPoemAsync([ActivityTrigger] ConversationContext conversationContext, FunctionContext executionContext)
        {
            var conversationResult = new ConversationResult();

            var chatClient = _clientProvider.GetChatClient();
            var builder = new ChatCompletionWithToolsBuilder(chatClient);
            builder.AddChatMessages(conversationContext.History.Convert()); // Chat Message from user

            var systemPrompt = $"You are a professional editor. Keep the poem, but can change the presentation."; // System Prompt

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
