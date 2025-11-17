namespace AiFuturesTerminal.Core.Strategy;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public sealed class StrategyWatchConfigService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public StrategyWatchConfigService(string filePath)
    {
        _filePath = filePath;
    }

    public StrategyWatchConfig LoadOrCreateDefault()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var cfg = JsonSerializer.Deserialize<StrategyWatchConfig>(json, _jsonOptions);
                if (cfg != null && cfg.Symbols != null && cfg.Symbols.Count > 0)
                    return cfg;
            }
        }
        catch
        {
            // ignore and fallback to defaults
        }

        var defaultCfg = new StrategyWatchConfig();
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "BTCUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "ETHUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "BNBUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "SOLUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "XRPUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "DOGEUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "ADAUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "LINKUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "LTCUSDT", Enabled = true, Kind = StrategyKind.ScalpingMomentum });
        defaultCfg.Symbols.Add(new WatchedSymbolConfig { Symbol = "OPUSDT",  Enabled = true, Kind = StrategyKind.ScalpingMomentum });

        Save(defaultCfg);
        return defaultCfg;
    }

    public void Save(StrategyWatchConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // swallow
        }
    }
}
