using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class RadioBrowserParseTests
{
    [Fact]
    public void Keeps_only_named_http_streams_and_dedupes()
    {
        var json = """
        [
          {"name":"A","url_resolved":"https://a.example/stream"},
          {"name":"A again","url_resolved":"HTTPS://A.EXAMPLE/stream"},
          {"name":"Bad","url_resolved":"file:///etc/passwd"},
          {"name":"","url_resolved":"https://b.example/s"},
          {"name":"NoUrl"}
        ]
        """;
        var list = RadioBrowserProvider.ParseStations(json);
        Assert.Single(list);
        Assert.Equal("A", list[0].Name);
    }
}