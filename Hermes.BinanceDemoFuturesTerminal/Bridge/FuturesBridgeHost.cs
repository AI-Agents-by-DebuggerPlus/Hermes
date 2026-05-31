using Hermes.BinanceDemoFuturesTerminal.ViewModels;

namespace Hermes.BinanceDemoFuturesTerminal.Bridge;

public sealed class FuturesBridgeHost : IDisposable
{
    private readonly FuturesBridgePublisher _publisher;
    private readonly FuturesBridgeCommandProcessor _processor;

    public FuturesBridgeHost(MainViewModel viewModel)
    {
        _publisher = new FuturesBridgePublisher(viewModel);
        _processor = new FuturesBridgeCommandProcessor(viewModel);
    }

    public void RequestPublish() => _publisher.RequestPublish();

    public void Dispose()
    {
        _publisher.Dispose();
        _processor.Dispose();
    }
}
