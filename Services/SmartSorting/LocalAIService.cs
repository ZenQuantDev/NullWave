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

    // FIX (C10): Read OLLAMA_HOST environment variable
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

    private volatile bool _isModelLoaded;
    public bool IsModelLoaded => _isModelLoaded;

    public event Action<string>? FallbackNotice;

    private readonly CancellationTokenSource _cts = new();

    // SINGLE UNIFIED CONSTRUCTOR
    public LocalAIService()
    {
        var host = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        _ollamaUrl = string.IsNullOrWhiteSpace(host) ? "http://localhost:11434" : host.TrimEnd('/');
        
        _ = StartQueueProcessorAsync();
        _ = StartHealingPingAsync(_cts.Token);
    }

    public void Shutdown() => _cts.Cancel();

    // FIX (C10): Complete the channel writer so the background processor loop exits cleanly
    public void Dispose()
    {
        _cts.Cancel();
        _stateQueue.Writer.TryComplete(); 
        _cts.Dispose();
    }
}