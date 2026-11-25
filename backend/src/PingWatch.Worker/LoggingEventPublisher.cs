using PingWatch.Application.Checks;
using PingWatch.Domain;

namespace PingWatch.Worker;

// Placeholder until M5: its alert dispatcher consumes these events.
internal sealed class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) : IDomainEventPublisher
{
    public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "{Event} for monitor {MonitorId}", domainEvent.GetType().Name, domainEvent.MonitorId);

        return ValueTask.CompletedTask;
    }
}