using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public enum FileOpType { Copy, Move, Delete }

public record FileOpRecord(DateTime Utc, FileOpType Type, string Source, string Dest, Guid TrackId);

/// <summary>
/// Records file system mutations (Copy, Move, Delete) to a bounded JSON journal.
/// Powers the "Undo last organize/import" feature and ensures D16/D17 data safety.
/// </summary>
public class FileOperationJournal
{
    private static readonly Lazy<FileOperationJournal> _instance = new(() => new FileOperationJournal());
    public static FileOperationJournal Instance => _instance.Value;

    private readonly string _path;
    private readonly List<FileOpRecord> _ops = new();
    private readonly object _lock = new();
    private const int MaxRecords = 1000;

    private FileOperationJournal()
    {
        _path = Path.Combine(NullWavePaths.DataDir, "file-ops.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var ops = JsonSerializer.Deserialize<List<FileOpRecord>>(json);
                if (ops != null) _ops.AddRange(ops);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[Journal] Failed to load file-ops.json"); }
    }

    private void Save()
    {
        try
        {
            // Keep only the most recent operations to prevent unbounded growth
            var recent = _ops.TakeLast(MaxRecords).ToList();
            var json = JsonSerializer.Serialize(recent, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex) { Log.Warning(ex, "[Journal] Failed to save file-ops.json"); }
    }

    public void Record(FileOpType type, string source, string dest, Guid trackId)
    {
        lock (_lock)
        {
            _ops.Add(new FileOpRecord(DateTime.UtcNow, type, source, dest, trackId));
            Save();
        }
    }

    public FileOpRecord? PopLast()
    {
        lock (_lock)
        {
            if (_ops.Count == 0) return null;
            var last = _ops[^1];
            _ops.RemoveAt(_ops.Count - 1);
            Save();
            return last;
        }
    }
}