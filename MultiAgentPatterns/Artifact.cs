using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiAgentPatterns
{
    public class Artifact
    {
        public string UserPrompt { get; set; }
        public List<string> Conversation { get; set; } = new List<string>();

        public string Answer { get; set; }
    }

    public class TokenStatistics
    {
        public int ToolToken { get; set; }
        public int SystemToken { get; set; }
        public int UserToken { get; set; }
        public int AssistantToken { get; set; }
        public int TotalToken => ToolToken + SystemToken + UserToken + AssistantToken;
    }
}
