using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.SmartSorting;

public partial class LocalAIService : IDisposable
{
    private static readonly HttpClient _pingClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly HttpClient _genClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim _aiEngineLock = new(1, 1);

    private readonly string _ollamaUrl;

    private readonly Channel<Func<Task>> _stateQueue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });

    private string _batteryModel = "qwen2.5:3b";
    private string _preferredPerformanceModel = "gemma3:4b";

    private volatile string _currentModel = "qwen2.5:3b";
    private volatile PowerState _currentPowerState = PowerState.AC;
    private volatile bool _autoPowerSwitch = true;

    private volatile bool _isReachable = true;
    public bool IsReachable => _isReachable;

    // FIX: Track if we have actually probed Ollama at least once.
    private volatile bool _reachabilityProbed;

    private volatile bool _isModelLoaded;
    public bool IsModelLoaded => _isModelLoaded;

    public event Action<string>? FallbackNotice;

    private readonly CancellationTokenSource _cts = new();

    public LocalAIService()
    {
        _ollamaUrl = OllamaEndpoint.Normalize(Environment.GetEnvironmentVariable("OLLAMA_HOST"));

        _ = StartQueueProcessorAsync();
        _ = StartHealingPingAsync(_cts.Token);
    }

    public string OllamaUrl => _ollamaUrl;

    public void Shutdown() => _cts.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _stateQueue.Writer.TryComplete();
        _cts.Dispose();
    }
}