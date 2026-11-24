using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NullWave.Helpers.Diagnostics;
using Serilog;

namespace NullWave.Helpers;

public static class CrashHandler
{
    public static void HandleFatalCrash(Exception ex)
    {
        try
        {
            Log.Fatal(ex, "Fatal application boundary crash intercepted.");
            
            var sb = new StringBuilder();
            sb.AppendLine("# NullWave Crash Report");
            sb.AppendLine($"**Time:** {DateTime.UtcNow:dd-MM-yyyy HH:mm:ss}");
            sb.AppendLine($"**Version:** {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            sb.AppendLine();
            sb.AppendLine("## What happened?");
            sb.AppendLine("NullWave encountered a critical error and could not start. We have saved the technical details below to help diagnose the issue.");
            sb.AppendLine();
            sb.AppendLine("## How to get help");
            sb.AppendLine("Please copy the text below and paste it into a GitHub Issue or the NullWave support thread.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("```text");
            // REDACT PATHS FROM STACK TRACE
            sb.AppendLine(DiagnosticsRedactor.RedactText(ex.ToString()));
            sb.AppendLine("```");

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var crashFile = Path.Combine(desktop, "NullWave-Crash-Report.md");
            
            File.AppendAllText(crashFile, sb.ToString() + Environment.NewLine + Environment.NewLine);
            ShellHelper.OpenFile(crashFile);
        }
        catch (Exception handlerEx)
        {
            try { File.WriteAllText("CRASH_HANDLER_FAILED.txt", handlerEx.ToString() + "\n\n" + ex.ToString()); } catch { }
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}