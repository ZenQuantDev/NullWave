using System.Collections.Generic;
using NullWave.Services;
using NullWave.Models;

namespace NullWave.Helpers;

internal static class YtDlpArgs
{
    public static void AppendNetworkArgs(List<string> args, Preferences p)
    {
        if (!string.IsNullOrWhiteSpace(p.YtDlpProxy))
        {
            args.Add("--proxy");
            args.Add(p.YtDlpProxy.Trim());
        }
        if (!string.IsNullOrWhiteSpace(p.YtDlpGeoProxy))
        {
            args.Add("--geo-verification-proxy");
            args.Add(p.YtDlpGeoProxy.Trim());
        }
    }
}