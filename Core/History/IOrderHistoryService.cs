namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IOrderHistoryService
{
    Task<IReadOnlyList<OrderHistoryRecord>> QueryOrdersAsync(HistoryQuery query, CancellationToken ct = default);
}