namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IPositionHistoryService
{
    Task<IReadOnlyList<PositionHistoryRecord>> QueryPositionsAsync(HistoryQuery query, CancellationToken ct = default);
}