using System;
using System.Globalization;
using System.Text.Json;

namespace NullWave.Helpers;

public static class BadgeRuleEvaluator
{
    public static bool IsEarned(BadgeRule rule, Func<string, double?> metric, DateTime? createdUtc)
    {
        if (rule.Metric == "createdBefore")
        {
            return createdUtc.HasValue
                && DateTime.TryParse(rule.GetStringValue(), CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var cutoff)
                && createdUtc.Value < cutoff;
        }

        if (metric(rule.Metric) is not double a) return false;

        double target;
        switch (rule.Value.ValueKind)
        {
            case JsonValueKind.True: target = 1; break;
            case JsonValueKind.False: target = 0; break;
            case JsonValueKind.Number: target = rule.Value.GetDouble(); break;
            default: return false;
        }

        return rule.Op switch
        {
            ">=" => a >= target,
            ">"  => a > target,
            "<=" => a <= target,
            "<"  => a < target,
            "==" => Math.Abs(a - target) < 1e-9,
            "!=" => Math.Abs(a - target) >= 1e-9,
            _ => false
        };
    }
}