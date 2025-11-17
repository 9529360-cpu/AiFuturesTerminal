namespace AiFuturesTerminal.Core.History;

using System.Threading;
using System.Threading.Tasks;

public interface IHistorySyncService
{
    Task SyncRecentTradesAsync(CancellationToken ct = default);
    Task SyncRecentOrdersAsync(CancellationToken ct = default);
}