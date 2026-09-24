using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.SmartSorting;

public partial class LocalAIService
{
    private async Task StartQueueProcessorAsync()
    {
        var reader = _stateQueue.Reader;
        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(out var stateMutationTask))
            {
                try { await stateMutationTask(); }
                catch (Exception ex) { Log.Error(ex, "[LocalAIService] Critical error during sequential state execution pipeline."); }
            }
        }
    }

    private async Task StartHealingPingAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(5), ct); }
            catch (TaskCanceledException) { break; }

            if (!_isReachable)
            {
                Log.Debug("[LocalAIService] Running background healing ping...");
                var isUp = await PingAsync();
                if (isUp)
                {
                    Log.Information("[LocalAIService] Healing ping succeeded. Local AI is back online.");
                    FallbackNotice?.Invoke("Local AI connection restored! AI features are back online.");
                }
            }
        }
    }

    public void ConfigurePowerModels(string batteryModel, string performanceModel, bool autoSwitch)
    {
        _stateQueue.Writer.TryWrite(async () =>
        {
            await UpdateStateAndApplyAsync("Configuration Update", () =>
            {
                _batteryModel = batteryModel;
                _preferredPerformanceModel = performanceModel;
                _autoPowerSwitch = autoSwitch;
            });
        });
    }

    public string CurrentModel
    {
        get => _currentModel;
        set
        {
            if (_autoPowerSwitch && _currentPowerState == PowerState.Battery)
            {
                Log.Debug("[LocalAIService] Ignoring CurrentModel override to '{Requested}' because system is on Battery power.", value);
                return;
            }

            var newValue = value?.Trim();
            if (string.IsNullOrEmpty(newValue)) return;

            if (!string.Equals(_currentModel, newValue, StringComparison.OrdinalIgnoreCase))
            {
                var oldModel = _currentModel;
                _currentModel = newValue;
                _stateQueue.Writer.TryWrite(async () =>
                {
                    await _aiEngineLock.WaitAsync();
                    try
                    {
                        Log.Warning("[LocalAIService] [Manual Override] Swapping models safely from '{Old}' to '{New}'...", oldModel, newValue);
                        if (!string.IsNullOrWhiteSpace(oldModel)) await UnloadModelAsync(oldModel);
                    }
                    finally { _aiEngineLock.Release(); }
                });
            }
        }
    }

    public void OnPowerStateChanged(PowerState state)
    {
        _stateQueue.Writer.TryWrite(async () =>
        {
            await UpdateStateAndApplyAsync($"Power Shift to {state}", () => _currentPowerState = state);
        });
    }

    private async Task UpdateStateAndApplyAsync(string contextSource, Action stateMutation)
    {
        await _aiEngineLock.WaitAsync();
        try
        {
            stateMutation();
            string? targetModel = (_autoPowerSwitch && _currentPowerState == PowerState.Battery) ? _batteryModel : _preferredPerformanceModel;
            targetModel = targetModel?.Trim();
            if (string.IsNullOrEmpty(targetModel)) return;

            if (!string.Equals(_currentModel, targetModel, StringComparison.OrdinalIgnoreCase))
            {
                var oldModel = _currentModel;
                _currentModel = targetModel;
                Log.Warning("[LocalAIService] [{Source}] Swapping models safely from '{Old}' to '{New}'...", contextSource, oldModel, targetModel);
                if (!string.IsNullOrWhiteSpace(oldModel)) await UnloadModelAsync(oldModel);
            }
        }
        finally { _aiEngineLock.Release(); }
    }
}