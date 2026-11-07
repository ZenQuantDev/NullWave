using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

var mode = Environment.GetEnvironmentVariable("FAKE_YTDLP_MODE") ?? "success";
var outDir = Environment.GetEnvironmentVariable("FAKE_YTDLP_OUTDIR") ?? Path.GetTempPath();
var argsFile = Environment.GetEnvironmentVariable("FAKE_YTDLP_ARGSFILE");

if (argsFile != null) File.WriteAllLines(argsFile, args);

switch (mode)
{
    case "success":
        Console.Error.WriteLine("[download] Destination: fake audio");
        Console.WriteLine("[download] 100% of 10.00MiB");
        var fakeFile = Path.Combine(outDir, "fake_track [abc123].mp3");
        File.WriteAllText(fakeFile, "fake audio data");
        Console.WriteLine(fakeFile);
        break;
    case "error":
        Console.Error.WriteLine("ERROR: Video unavailable");
        Environment.Exit(1);
        break;
    case "hang":
        Console.Error.WriteLine("Hanging forever...");
        Thread.Sleep(Timeout.Infinite);
        break;
    case "no_output":
        Environment.Exit(0);
        break;
    case "duplicate":
        Console.WriteLine("Already downloaded");
        Environment.Exit(0);
        break;
    case "metadata":
        Console.WriteLine("Fake Title");
        Console.WriteLine("Fake Artist");
        Console.WriteLine("NA");
        Console.WriteLine("245.5");
        break;
    case "metadata_na":
        for (var i = 0; i < 4; i++) Console.WriteLine("NA");
        break;
}