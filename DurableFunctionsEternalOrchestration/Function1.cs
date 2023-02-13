using DurableTask.Core;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableFunctionsEternalOrchestration
{
    public class Function1
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<Function1> _logger;
        private const string InstanceId = "MyInstance2";

        public Function1(ILogger<Function1> logger, TelemetryClient telemetryClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName("Function1")]
        public async Task Run([TimerTrigger("*/5 * * * * *")]TimerInfo myTimer, [DurableClient]IDurableClient starter)
        {
            // Check if an instance with the specified ID already exists or an existing one stopped running(completed/failed/terminated).
            var existingInstance = await starter.GetStatusAsync(InstanceId);
            if (existingInstance != null
                && existingInstance.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                && existingInstance.RuntimeStatus != OrchestrationRuntimeStatus.Failed
                && existingInstance.RuntimeStatus != OrchestrationRuntimeStatus.Terminated)
            {
                return;
            }

            await starter.StartNewAsync("Orchestrator", InstanceId);

            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        }

        [FunctionName("Orchestrator")]
        public async Task RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (context.InstanceId != InstanceId)
            {
                if (context.IsReplaying)
                    _logger.LogDebug("Stopping instance {instanceId} because it is not the active: {activeInstanceId}", context.InstanceId, InstanceId);

                return;
            }

            if (!context.IsReplaying)
            {
                _logger.LogDebug("Starting Orchestrator flow in instance {instanceId}..", context.InstanceId);
            }

            await context.CallSubOrchestratorAsync("SubOrchestrator", $"Sub_{context.NewGuid()}");
        }

        [FunctionName("SubOrchestrator")]
        public async Task RunSubOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (CorrelationTraceContext.Current is null)
            {
                throw new InvalidOperationException($"This sample expects a correlation trace context of {nameof(W3CTraceContext)}, but the context is null.");
            }

            if (CorrelationTraceContext.Current is not W3CTraceContext correlationContext)
            {
                throw new InvalidOperationException($"This sample expects a correlation trace context of {nameof(W3CTraceContext)}, but the context is of type {CorrelationTraceContext.Current.GetType()}");
            }

            // Setup tracing before we validate input because this will allow any errors to be linked to the tracing timeline.
            var trace = new TraceTelemetry($"Activity Id: {correlationContext.TraceParent} ParentSpanId: {correlationContext.ParentSpanId}");
            trace.Context.Operation.Id = correlationContext.TelemetryContextOperationId;
            trace.Context.Operation.ParentId = correlationContext.TelemetryContextOperationParentId;
            _telemetryClient.Track(trace);

            if (!context.IsReplaying)
            {
                _logger.LogInformation("Starting sub-orchestration..");
            }

            await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(5), CancellationToken.None);

            if (!context.IsReplaying)
            {
                _logger.LogInformation("Finished sub-orchestration..");
            }
        }
    }
}
