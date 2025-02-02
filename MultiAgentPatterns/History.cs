using OpenAI.Chat;

namespace MultiAgentPatterns
{
    public class History
    {
        public History()
        {
            // For serialization
        }

        public History(string prompt, MessageType messageType)
        {
            Prompt = prompt;
            MessageType = messageType;
        }

        public string Prompt { get; set; }
        public MessageType MessageType { get; set; }
    }

    public enum MessageType
    {
        System,
        User,
        Tool,
        Assistant,
    }

    public static class HisotryExtensions
    {
        public static List<ChatMessage> Convert(this List<History> histories)
        {
            return histories.Select(p => p.Convert()).ToList();
        }

        private static ChatMessage Convert(this History history)
        {
            switch (history.MessageType)
            {
                case MessageType.System:
                    return new SystemChatMessage(history.Prompt);
                case MessageType.User:
                    return new UserChatMessage(history.Prompt);
                case MessageType.Tool:
                    return new ToolChatMessage(history.Prompt);
                case MessageType.Assistant:
                    return new AssistantChatMessage(history.Prompt);
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
