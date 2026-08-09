using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace NullWave.Helpers.Logging;

public class ApiKeyRedactionEnricher : ILogEventEnricher
{
    // Only match actual API key patterns, not arbitrary hex strings
    private static readonly Regex KeyPattern = new(
        @"\b(AIzaSy[A-Za-z0-9_-]{33}|[a-f0-9]{32})(?=\s|$|[^a-f0-9])", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties)
        {
            if (property.Value is ScalarValue scalar && scalar.Value is string stringValue)
            {
                // Skip URLs - they're not API keys
                if (stringValue.Contains("://")) continue;
                
                if (KeyPattern.IsMatch(stringValue))
                {
                    var redacted = KeyPattern.Replace(stringValue, "[REDACTED_API_KEY]");
                    logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(redacted)));
                }
            }
        }
    }
}