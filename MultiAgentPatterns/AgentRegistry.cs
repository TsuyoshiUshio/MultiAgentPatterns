using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiAgentPatterns
{
    public class AgentRegistry
    {
        public Dictionary<string, IAgent> Agents { get; private set; } = new Dictionary<string, IAgent>();
        public AgentRegistry(IEnumerable<IAgent> agents)
        {
            foreach (var agent in agents)
            {
                Agents.Add(agent.Name, agent);
            }
        }

        public IAgent GetAgent(string name)
        {
            return Agents[name];
        }
    }
}
