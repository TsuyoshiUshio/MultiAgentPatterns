using Microsoft.DurableTask;

namespace MultiAgentPatterns
{
    public interface IAgent
    {
        string Name { get; }
        string Description { get; }
        Task<ConversationResult> StartAsync(TaskOrchestrationContext orchestrationContext, ConversationContext conversationContext);
    }
}
