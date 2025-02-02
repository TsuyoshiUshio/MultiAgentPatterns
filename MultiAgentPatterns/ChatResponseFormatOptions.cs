namespace MultiAgentPatterns
{
    public class ChatResponseFormatOptions
    {
        public string JsonSchemaFormatName { get; set; }
        public BinaryData JsonSchema { get; set; }
        public string JsonSchemaFormatDescription { get; set; } = null;
        public bool? JsonSchemaIsStrict { get; set; } = null;
    }
}
