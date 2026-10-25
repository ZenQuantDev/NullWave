using System;
using System.Text.Json;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class BadgeRuleEvaluatorTests
{
    private static BadgeRule Rule(string metric, string op, string json) =>
        new() { Metric = metric, Op = op, Value = JsonDocument.Parse(json).RootElement.Clone() };

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void Numeric_threshold(double actual, bool expected) =>
        Assert.Equal(expected, BadgeRuleEvaluator.IsEarned(Rule("m", ">=", "100"), _ => actual, null));

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Bool_rule(double actual, bool expected) =>
        Assert.Equal(expected, BadgeRuleEvaluator.IsEarned(Rule("m", "==", "true"), _ => actual, null));

    [Fact]
    public void Unknown_metric_is_never_earned() =>
        Assert.False(BadgeRuleEvaluator.IsEarned(Rule("nope", ">=", "0"), _ => null, null));

    [Fact]
    public void CreatedBefore_compares_in_utc()
    {
        var rule = Rule("createdBefore", "", "\"2026-08-01\"");
        Assert.True(BadgeRuleEvaluator.IsEarned(rule, _ => null, new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Utc)));
        Assert.False(BadgeRuleEvaluator.IsEarned(rule, _ => null, new DateTime(2026, 8, 1, 0, 0, 1, DateTimeKind.Utc)));
        Assert.False(BadgeRuleEvaluator.IsEarned(rule, _ => null, null));
    }
}