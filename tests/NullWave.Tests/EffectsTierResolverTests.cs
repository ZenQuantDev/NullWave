using NullWave.Models;
using NullWave.Services;
using NullWave.Services.SmartSorting;

namespace NullWave.Tests.SmartSorting;

public class EffectsTierResolverTests
{
    private static PreferencesService NewPrefs(bool auto = true, string manual = "Standard")
    {
        var prefs = new PreferencesService();
        prefs.Current.AutoEffectsTier = auto;
        prefs.Current.EffectsTier = manual;
        return prefs;
    }

    [Fact]
    public void Auto_tier_is_Minimal_while_detection_is_pending()
    {
        using var prefs = NewPrefs();
        var resolver = new EffectsTierResolver(prefs, () => null, post: a => a());

        Assert.Equal(EffectsTier.Minimal, resolver.Current);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Auto_tier_is_Standard_with_a_discrete_gpu(bool nvidia, bool amd)
    {
        using var prefs = NewPrefs();
        var info = new HardwareInfo { HasNvidia = nvidia, HasAmd = amd };
        var resolver = new EffectsTierResolver(prefs, () => info, post: a => a());

        Assert.Equal(EffectsTier.Standard, resolver.Current);
    }

    [Fact]
    public void Auto_tier_is_Minimal_without_a_discrete_gpu()
    {
        using var prefs = NewPrefs();
        var resolver = new EffectsTierResolver(prefs, () => new HardwareInfo(), post: a => a());

        Assert.Equal(EffectsTier.Minimal, resolver.Current);
    }

    [Fact]
    public void Manual_tier_ignores_hardware()
    {
        using var prefs = NewPrefs(auto: false, manual: "Minimal");
        var info = new HardwareInfo { HasNvidia = true };
        var resolver = new EffectsTierResolver(prefs, () => info, post: a => a());

        Assert.Equal(EffectsTier.Minimal, resolver.Current);
    }

    [Fact]
    public void Dev_override_beats_everything_and_notifies()
    {
        using var prefs = NewPrefs(auto: true);
        var resolver = new EffectsTierResolver(prefs, () => null, post: a => a());
        var events = new List<string?>();
        resolver.PropertyChanged += (_, e) => events.Add(e.PropertyName);

        resolver.SetDevOverride(EffectsTier.Standard);

        Assert.Equal(EffectsTier.Standard, resolver.Current);
        Assert.Contains(nameof(EffectsTierResolver.Current), events);
    }

    [Fact]
    public void Battery_override_applies_only_in_auto_mode()
    {
        var info = new HardwareInfo { HasNvidia = true };

        using (var auto = NewPrefs(auto: true))
        {
            var resolver = new EffectsTierResolver(auto, () => info, post: a => a());
            resolver.SetBatteryOverride(EffectsTier.Minimal);
            Assert.Equal(EffectsTier.Minimal, resolver.Current);

            resolver.ClearBatteryOverride();
            Assert.Equal(EffectsTier.Standard, resolver.Current);
        }

        using (var manual = NewPrefs(auto: false, manual: "Standard"))
        {
            var resolver = new EffectsTierResolver(manual, () => info, post: a => a());
            resolver.SetBatteryOverride(EffectsTier.Minimal);
            Assert.Equal(EffectsTier.Standard, resolver.Current);
        }
    }

    [Fact]
    public async Task Finished_detection_notifies_once_and_the_tier_updates()
    {
        using var prefs = NewPrefs();
        HardwareInfo? detected = null;
        var resolver = new EffectsTierResolver(prefs, () => detected, post: a => a());
        var events = new List<string?>();
        resolver.PropertyChanged += (_, e) => events.Add(e.PropertyName);

        var detection = new TaskCompletionSource<HardwareInfo>();
        var continuation = resolver.AttachDetection(detection.Task);

        Assert.Equal(EffectsTier.Minimal, resolver.Current);

        detected = new HardwareInfo { HasNvidia = true };
        detection.SetResult(detected);
        await continuation;

        Assert.Equal(new[] { nameof(EffectsTierResolver.Current) }, events);
        Assert.Equal(EffectsTier.Standard, resolver.Current);
    }

    [Fact]
    public async Task Failed_detection_does_not_notify_throw_or_change_the_tier()
    {
        using var prefs = NewPrefs();
        var resolver = new EffectsTierResolver(prefs, () => null, post: a => a());
        var events = new List<string?>();
        resolver.PropertyChanged += (_, e) => events.Add(e.PropertyName);

        var detection = new TaskCompletionSource<HardwareInfo>();
        var continuation = resolver.AttachDetection(detection.Task);
        detection.SetException(new InvalidOperationException("boom"));

        await continuation;

        Assert.Empty(events);
        Assert.Equal(EffectsTier.Minimal, resolver.Current);
    }

    [Fact]
    public async Task Notification_goes_through_the_ui_poster()
    {
        using var prefs = NewPrefs();
        var posted = 0;
        var resolver = new EffectsTierResolver(prefs, () => new HardwareInfo { HasNvidia = true },
            post: a => { posted++; a(); });

        await resolver.AttachDetection(Task.FromResult(new HardwareInfo { HasNvidia = true }));

        Assert.Equal(1, posted);
    }
}