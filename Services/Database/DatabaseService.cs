using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public partial class DatabaseService : IDisposable
{
    private readonly SQLiteConnection _db;
    private readonly PreferencesService? _prefs;
    private bool _disposed;
    private bool _ftsAvailable;
    public string DbPath { get; }

    private readonly object _dbLock = new();
    private readonly object _writeLock = new();
    private readonly Dictionary<Guid, Track> _pendingWrites = new();
    private readonly Channel<Guid> _writeSignal = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });
    private Task? _writerTask;
    private readonly CancellationTokenSource _migrationCts = new();

    public int SchemaVersion
    {
        get { lock (_dbLock) return _db.ExecuteScalar<int>("PRAGMA user_version;"); }
    }

    public DatabaseService(PreferencesService? prefs = null)
    {
        _prefs = prefs;
        var path = NullWavePaths.DatabasePath;
        DbPath = path;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        ApplyPendingRestore(path);
        MigrateLegacyDatabaseIfNeeded(path);
        CreateRollingBackupIfNeeded(path);

        _db = new SQLiteConnection(path);

        _db.ExecuteScalar<string>("PRAGMA journal_mode=WAL;");
        _db.Execute("PRAGMA synchronous=NORMAL;");

        _db.CreateTable<TrackRecord>();
        _db.CreateTable<PlaylistRecord>();
        _db.CreateTable<PlaylistTrackRecord>();
        _db.CreateTable<PlaylistFolderRecord>();

        MigrateSchema();
        EnsureFts5Index();

        Log.Information("[DatabaseService] Opened DB at {Path} with WAL mode enabled", path);
        
        _writerTask = Task.Run(WriterLoop);
    }

    public void UpdateAsync(Track track)
    {
        bool signal;
        lock (_writeLock)
        {
            signal = !_pendingWrites.ContainsKey(track.Id);
            _pendingWrites[track.Id] = track;
        }
        if (signal) _writeSignal.Writer.TryWrite(track.Id);
    }

    private async Task WriterLoop()
    {
        await foreach (var id in _writeSignal.Reader.ReadAllAsync())
        {
            lock (_dbLock)
            {
                Track? track;
                lock (_writeLock) _pendingWrites.Remove(id, out track);
                if (track == null) continue;
                try { SaveTrackRecord(TrackRecord.FromTrack(track)); }
                catch (Exception ex) { Log.Error(ex, "[DatabaseService] Queued write failed for {Id}", id); }
            }
        }
    }

    public void FlushWrites()
    {
        while (true)
        {
            lock (_writeLock) { if (_pendingWrites.Count == 0) break; }
            Thread.Sleep(5);
        }
        lock (_dbLock) { }
    }

    public List<Track> LoadAll()
    {
        lock (_dbLock)
        {
            try
            {
                return _db.Table<TrackRecord>().ToList().Select(r => r.ToTrack()).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DatabaseService] Failed to load tracks");
                return new List<Track>();
            }
        }
    }

    public void Insert(Track track)
    {
        try { SaveTrackRecord(TrackRecord.FromTrack(track)); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Insert failed for {Title}", track.Title); }
    }

    public void Update(Track track)
    {
        try { SaveTrackRecord(TrackRecord.FromTrack(track)); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Update failed for {Title}", track.Title); }
    }

    private void SaveTrackRecord(TrackRecord record)
    {
        lock (_dbLock)
        {
            _db.RunInTransaction(() =>
            {
                if (_db.Find<TrackRecord>(record.Id) == null) _db.Insert(record);
                else _db.Update(record);
            });
        }
    }

    public void Delete(Guid id)
    {
        lock (_dbLock)
        {
            lock (_writeLock) _pendingWrites.Remove(id);
            try { _db.Delete<TrackRecord>(id.ToString()); }
            catch (Exception ex) { Log.Error(ex, "[DatabaseService] Delete failed for {Id}", id); }
        }
    }

    public void RunInTransaction(Action action)
    {
        lock (_dbLock)
        {
            try { _db.RunInTransaction(action); }
            catch (Exception ex) { Log.Error(ex, "[DatabaseService] Transaction failed"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _migrationCts.Cancel();
        _writeSignal.Writer.TryComplete();
        try { _writerTask?.Wait(2000); } catch { }
        lock (_dbLock)
        {
            try { _db.ExecuteScalar<string>("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { }
            _db.Close();
        }
        _migrationCts.Dispose();
    }
}