using Pos.SharedKernel.Events;

namespace Pos.Tests.SharedKernel;

public sealed class InProcessEventBusTests
{
    private sealed record TestEvent(string Message);

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_ShouldNotThrow()
    {
        var bus = new InProcessEventBus();

        var act = () => bus.PublishAsync(new TestEvent("hello"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverEvent()
    {
        var bus = new InProcessEventBus();
        TestEvent? received = null;

        bus.Subscribe<TestEvent>((evt, _) =>
        {
            received = evt;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("hello"));

        // Give async delivery a moment
        await Task.Delay(100);

        received.Should().NotBeNull();
        received!.Message.Should().Be("hello");
    }

    [Fact]
    public async Task Subscribe_ShouldReturnDisposable_ThatUnsubscribes()
    {
        var bus = new InProcessEventBus();
        var callCount = 0;

        var subscription = bus.Subscribe<TestEvent>((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("first"));
        await Task.Delay(100);

        subscription.Dispose();

        await bus.PublishAsync(new TestEvent("second"));
        await Task.Delay(100);

        callCount.Should().Be(1);
    }
}
