using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public enum FileOpType { Copy, Move, Delete }

public record FileOpRecord(DateTime Utc, FileOpType Type, string Source, string Dest, Guid TrackId);

/// <summary>
/// Append-only log of file-system mutations. Records (UTC, type, source, dest, trackId)
/// to a bounded JSON file. Does NOT implement undo (yet) - this is the audit trail that
/// a future "Undo last import/organize" feature will consume. D16/D17 data-safety evidence.
/// </summary>
public class FileOperationJournal
{
    private static readonly Lazy<FileOperationJournal> _instance = new(() => new FileOperationJournal());
    public static FileOperationJournal Instance => _instance.Value;

    private readonly string _path;
    private readonly List<FileOpRecord> _ops = new();
    private readonly object _lock = new();
    private const int MaxRecords = 1000;

    // Debounced flush: every Record() re-arms the timer; fires 2 s after the last mutation.
    // Avoids rewriting the file on every record during a 5k-track import.
    private Timer? _flushTimer;
    private bool _dirty;

    private FileOperationJournal()
    {
        _path = Path.Combine(NullWavePaths.DataDir, "file-ops.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return;
            var ops = JsonSerializer.Deserialize<List<FileOpRecord>>(json);
            if (ops != null) _ops.AddRange(ops);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "[Journal] file-ops.json is corrupt; quarantining and starting fresh");
            try { File.Move(_path, _path + ".bad-" + UtcTime.FileStamp()); } catch { }
        }
        catch (Exception ex) { Log.Warning(ex, "[Journal] Failed to load file-ops.json"); }
    }

    private void Save()
    {
        try
        {
            var recent = _ops.TakeLast(MaxRecords).ToList();
            var json = JsonSerializer.Serialize(recent, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) { Log.Warning(ex, "[Journal] Failed to save file-ops.json"); }
    }

    public void Record(FileOpType type, string source, string dest, Guid trackId)
    {
        lock (_lock)
        {
            _ops.Add(new FileOpRecord(DateTime.UtcNow, type, source, dest, trackId));
            _dirty = true;
            // Debounce: re-arm the timer on every mutation. Flush happens 2 s after the last one.
            _flushTimer ??= new Timer(_ => FlushIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
            _flushTimer.Change(2000, Timeout.Infinite);
        }
    }

    /// <summary>Called by the caller at shutdown to guarantee the journal is on disk before exit.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            if (!_dirty) return;
            Save();
            _dirty = false;
        }
    }

    private void FlushIfDirty()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            Save();
            _dirty = false;
        }
    }

}