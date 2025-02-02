
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiAgentPatterns
{
    public class ConversationContext
    {
        public string GroupConversationUserPrompt { get; set; }
        public string RequestedAgent { get; set; }
        public string UserPrompt { get; set; }
        public List<History> History { get; set; }
    }
}
