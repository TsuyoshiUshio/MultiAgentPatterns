using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;

namespace MultiAgentPatterns
{
    public class RequestContext : TaskEntity<RequestContextState>
    {
        public void Add(RequestContextState value) => this.State = value;
        public void Reset() => this.State = new RequestContextState();

        public RequestContextState Get() => this.State;

        public void CleanUp(DateTimeOffset lastSignaled)
        {
            if (State.LastUpdated == lastSignaled)
            {
                // Delete Operation
                // https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-dotnet-entities?pivots=isolated#deleting-entities-in-the-isolated-model
                this.State = null;
            }
        }

        [Function(nameof(RequestContext))]
        public Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync(this);
        }
    }

    // TODO modify the History not only text but also the kind of message
    public class RequestContextState
    {
        public Artifact Artifact { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
    }
}
