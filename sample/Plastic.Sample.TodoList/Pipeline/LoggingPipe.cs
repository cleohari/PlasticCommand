﻿namespace Plastic.Sample.TodoList.Pipeline
{
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public class LoggingPipe : Pipe
    {
        private readonly ILogger<LoggingPipe> _logger;

        public LoggingPipe(ILogger<LoggingPipe> logger)
        {
            this._logger = logger;
        }

        public async override Task<ExecutionResult> Handle(
            PipelineContext context, Behavior<ExecutionResult> nextBehavior, CancellationToken token)
        {
            this._logger.LogInformation($"Execute Command - {context.CommandSpec.Name}");
            this._logger.LogInformation($"Parameter - {context.Parameter?.ToString()}");

            ExecutionResult result = await nextBehavior.Invoke().ConfigureAwait(false);

            this._logger.LogInformation($"Result - {result}");

            return result;
        }
    }
}
