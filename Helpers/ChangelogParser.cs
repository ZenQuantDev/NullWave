using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NullWave.Helpers;

public static class ChangelogParser
{
    public static string GetLatestReleaseNotes()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("CHANGELOG.md"));
                
            if (resourceName == null) return "Release notes not found.";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return "Could not read release notes.";
            
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            
            var lines = content.Split('\n');
            var result = new StringBuilder();
            bool inSection = false;
            
            foreach (var line in lines)
            {
                if (line.StartsWith("## ["))
                {
                    if (inSection) break; // Stop at the next version header
                    inSection = true;
                }
                
                if (inSection) result.AppendLine(line.TrimEnd('\r'));
            }
            
            return result.ToString().Trim();
        }
        catch
        {
            return "Could not load release notes.";
        }
    }
}