using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace NullWave.Helpers.Logging;

public class ApiKeyRedactionEnricher : ILogEventEnricher
{
    private static readonly Regex KeyPattern = new(
        @"\b(AIzaSy[A-Za-z0-9_-]{33}|[a-f0-9]{32})\b", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties)
        {
            if (property.Value is ScalarValue scalar && scalar.Value is string stringValue)
            {
                if (KeyPattern.IsMatch(stringValue))
                {
                    var redacted = KeyPattern.Replace(stringValue, "[REDACTED_API_KEY]");
                    logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(redacted)));
                }
            }
        }
    }
}