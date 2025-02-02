namespace MultiAgentPatterns
{
    public class ConversationResult
    {
        public bool Approved { get; set; }
        public List<History> NewHistory { get; set; } = new ();

        public string Text { get; set; }
    }
}
