using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;

namespace MultiAgentPatterns
{
    public class SessionHistory : TaskEntity<SessionHistoryState>
    {
        public void Add(SessionHistoryState value) => this.State = value;
        public void Reset() => this.State = new SessionHistoryState();

        public SessionHistoryState Get() => this.State;

        public void CleanUp(DateTimeOffset lastSignaled)
        {
            if (State.LastUpdated == lastSignaled)
            {
                // Delete Operation
                // https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-dotnet-entities?pivots=isolated#deleting-entities-in-the-isolated-model
                this.State = null;
            }
        }

        [Function(nameof(SessionHistory))]
        public Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync(this);
        }
    }

    // TODO modify the History not only text but also the kind of message
    public class SessionHistoryState
    {
        public List<History> History { get; set; } = new List<History>();
        public DateTimeOffset LastUpdated { get; set; }
    }
}
