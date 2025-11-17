namespace AiFuturesTerminal.Core.History;

using System.Threading;
using System.Threading.Tasks;

public sealed class NoopHistorySyncService : IHistorySyncService
{
    public Task SyncRecentTradesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SyncRecentOrdersAsync(CancellationToken ct = default) => Task.CompletedTask;
}