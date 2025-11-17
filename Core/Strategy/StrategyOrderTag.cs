namespace AiFuturesTerminal.Core.Strategy;

using System;

public static class StrategyOrderTag
{
    public static string BuildClientOrderId(string strategyId, string runId, int seq)
    {
        return $"STRAT:{strategyId}|RUN:{runId}|SEQ:{seq}";
    }

    public static bool TryParse(string clientOrderId, out string? strategyId, out string? runId, out int? seq)
    {
        strategyId = null;
        runId = null;
        seq = null;
        if (string.IsNullOrWhiteSpace(clientOrderId)) return false;

        try
        {
            var parts = clientOrderId.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var kv = p.Split(':', 2);
                if (kv.Length != 2) continue;
                var key = kv[0];
                var val = kv[1];
                if (key == "STRAT") strategyId = val;
                else if (key == "RUN") runId = val;
                else if (key == "SEQ")
                {
                    if (int.TryParse(val, out var s)) seq = s;
                }
            }

            return strategyId != null;
        }
        catch
        {
            return false;
        }
    }
}