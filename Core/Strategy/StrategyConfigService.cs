namespace AiFuturesTerminal.Core.Strategy;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class StrategyConfigService
{
    private readonly string _configPath;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true
    };

    public StrategyConfigService(string? configPath = null)
    {
        _configPath = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(AppContext.BaseDirectory, "strategy_config.json")
            : configPath!;
    }

    public async Task<StrategyConfig> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_configPath))
                return new StrategyConfig();

            await using var stream = File.OpenRead(_configPath);
            var cfg = await JsonSerializer.DeserializeAsync<StrategyConfig>(stream, _options, ct)
                      .ConfigureAwait(false);
            return cfg ?? new StrategyConfig();
        }
        catch
        {
            // 配置坏掉时，用默认值继续跑，避免整个程序崩溃。
            return new StrategyConfig();
        }
    }

    public async Task SaveAsync(StrategyConfig config, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, _options, ct).ConfigureAwait(false);
    }
}
