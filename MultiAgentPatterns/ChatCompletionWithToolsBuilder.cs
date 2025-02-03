using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.ClientModel;
using System.Diagnostics;


namespace MultiAgentPatterns
{
    public class ChatCompletionWithToolsBuilder
    {
        ChatClient _chatClient;
        List<ChatTool> _tools = new List<ChatTool>();
        private Func<ChatToolCall, List<ChatMessage>, Task<List<ChatMessage>>> _toolCallSection;
        List<ChatMessage> _chatMessages = new List<ChatMessage>();

        ChatResponseFormatOptions _chatResponseFormatOptions = null;

        ILogger _logger;

        public List<ChatTool> ChatTools => _tools;

        public void AddChatMessages(IList<ChatMessage> messages)
        {
            _chatMessages.AddRange(messages);
        }

        public void AddChatTool(ChatTool tool)
        {
            _tools.Add(tool);
        }

        public void AddChatMessage(ChatMessage chatMessage)
        {
            _chatMessages.Add(chatMessage);
        }

        public void AddSystemMessage(string message)
        {
            if (!string.IsNullOrEmpty(message))
                _chatMessages.Add(new SystemChatMessage(message));
        }

        public void AddUserMessage(string message)
        {
            _chatMessages.Add(new UserChatMessage(message));
        }

        // Enable StructuredOutput with the format.
        public void SetResponseFormat(ChatResponseFormatOptions chatResponseFormatOptions)
        {
            _chatResponseFormatOptions = chatResponseFormatOptions;
        }

        public List<ChatMessage> ChatMessages => _chatMessages;

        public void SetFunctionCallSection(Func<ChatToolCall, List<ChatMessage>, Task<List<ChatMessage>>> toolCallSection)
        {
            _toolCallSection = toolCallSection;
        }

        public ChatCompletionWithToolsBuilder(ChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public void AddLogger(ILogger logger)
        {
            _logger = logger;
        }

        private bool ShouldRunTool(ChatFinishReason finishReason)
        {
            return finishReason == ChatFinishReason.ToolCalls;
        }

        public async Task<ChatCompletionExecutionResult> ExecuteWithRetryAsync()
        {
            int maxRetry = 3;
            for (int retry = 1; retry <= maxRetry; retry++)
            {
                try
                {
                    return await ExecuteAsync();
                }
                catch (Exception e)
                {
                    // Currently, we only handle the content_filter exception.
                    if (e is ClientResultException ce)
                    {
                        if (ce.Status == 400 && ce.Message.Contains("content_filter"))
                        {
                            // Hit the conteint_filter. It is not hit the content filter but throttled.
                            if (_logger != null) _logger.LogWarning($"Hit the content_filter. Retry {retry} time. Message: {e.Message}");
                            if (retry == maxRetry) throw;

                            await ValidateAndUpdateUserPromptAsync();
                            continue;
                        }
                    }
                    throw;
                }
            }

            throw new Exception($"Retry {maxRetry} times, still hit the content_filter issue. Contact with the administrator.");
        }

        private async Task ValidateAndUpdateUserPromptAsync()
        {
            // Take the UserPrompt Make sure,
            string lastUserPrompt = string.Empty;
            int lastUserPromptIndex = 0;
            for (int i = 0; i < _chatMessages.Count(); i++)
            {
                var chatMessage = _chatMessages[i];
                if (chatMessage is UserChatMessage userChatMessage)
                {
                    lastUserPrompt = userChatMessage.Content.FirstOrDefault().Text;
                    lastUserPromptIndex = i;
                }
            }

            if (_logger != null) _logger.LogWarning($"ValidateAndUpdateUserPromptAsync: {lastUserPrompt}");

            // Instruct the UserPrompt and ask to rewrite the UserPrompt not violate the content_filter.
            var instruction = $"Please check each incoming request for disallowed content. If you detect any violation of the content filter, remove or replace the problematic sections. Return only the sanitized version of the request without any additional explanation or quotation marks. \n\n UserPrompt: `{lastUserPrompt}`";
            var previousAssistantMessage = "The response was filtered due to the prompt triggering Azure OpenAI's content management policy. Please modify your prompt and retry. To learn more about our content filtering policies please read our documentation: https://go.microsoft.com/fwlink/?linkid=2198766";
            List<ChatMessage> messages = new List<ChatMessage>();
            messages.Add(instruction);

            ChatCompletionOptions chatCompletionOptions = new()
            {
                Temperature = 0
            };

            string updatedUserPrompt = string.Empty;
            try
            {
                var result = await _chatClient.CompleteChatAsync(messages, chatCompletionOptions);
                updatedUserPrompt = result.Value.Content[0].Text;
            }
            catch (Exception e)
            {
                throw;
            }


            if (_logger != null) _logger.LogWarning($"ValidateAndUpdateUserPromptAsync updatedUserPrompt: {updatedUserPrompt}");

            List<ChatMessage> updatedMessages = new List<ChatMessage>();

            for (int l = 0; l < _chatMessages.Count(); l++)
            {
                if (l == lastUserPromptIndex)
                {
                    updatedMessages.Add(new UserChatMessage(updatedUserPrompt));
                }
                else
                {
                    updatedMessages.Add(_chatMessages[l]);
                }
            }
            _chatMessages = updatedMessages;
        }


        public async Task<ChatCompletionExecutionResult> ExecuteAsync()
        {
            // Create a new object fore enabling retry.
            List<ChatMessage> chatMessages = new List<ChatMessage>(_chatMessages);

            ChatCompletionOptions chatCompletionOptions = new()
            {
                Temperature = 0
            };

            if (_chatResponseFormatOptions != null)
            {
                chatCompletionOptions.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    _chatResponseFormatOptions.JsonSchemaFormatName,
                    _chatResponseFormatOptions.JsonSchema,
                    _chatResponseFormatOptions.JsonSchemaFormatDescription,
                    _chatResponseFormatOptions.JsonSchemaIsStrict);
            }

            var chatCompletionResult = new ChatCompletionExecutionResult();

            Stopwatch stopwatch = new();
            stopwatch.Start();

            foreach (var tool in _tools)
            {
                chatCompletionOptions.Tools.Add(tool);
            }
            chatCompletionResult.TokenStatistics = await TokenUtils.GetAndValidateChatCompletionTokensStatistics(chatMessages);
            var result = await _chatClient.CompleteChatAsync(chatMessages, chatCompletionOptions);
            ChatCompletion completion = result.Value;
            stopwatch.Stop();


            while (ShouldRunTool(completion.FinishReason))
            {
                chatMessages.Add(new AssistantChatMessage(completion));

                foreach (ChatToolCall toolCall in completion.ToolCalls)
                {
                    chatMessages = await _toolCallSection(toolCall, chatMessages);
                }
                try
                {
                    var tokenStatistics = await TokenUtils.GetAndValidateChatCompletionTokensStatistics(chatMessages);
                    chatCompletionResult.AddToolCallTokenStatistics(tokenStatistics);
                }
                catch (TokenLengthException e)
                {
                    string message = $"Token Length Exceeded. Last chat message has been removed. Reduce the number of rows or provide the analysis by the given information.";
                    ChatMessage removedElement = null;
                    if (chatMessages.Count > 0)
                    {
                        removedElement = chatMessages[chatMessages.Count - 1];
                        chatMessages.RemoveAt(chatMessages.Count - 1);
                    }
                    if (removedElement != null)
                    {
                        if (removedElement is ToolChatMessage removedToolChainMessage)
                        {
                            chatMessages.Add(new ToolChatMessage(removedToolChainMessage.ToolCallId, message));
                        }
                        else
                        {
                            chatMessages.Add(new AssistantChatMessage(message));
                        }
                    }
                }

                result = await _chatClient.CompleteChatAsync(chatMessages, chatCompletionOptions);
                completion = result.Value;
            }
            chatCompletionResult.Result = completion.Content[0].Text;
            return chatCompletionResult;
        }
    }

    public class ChatCompletionExecutionResult
    {
        public string Result { get; set; }

        public TokenStatistics TokenStatistics { get; set; }
        public IList<TokenStatistics> ToolCallTokenStatistics { get; set; } = new List<TokenStatistics>();

        public void AddToolCallTokenStatistics(TokenStatistics tokenStatistics)
        {
            ToolCallTokenStatistics.Add(tokenStatistics);
        }
    }
}
