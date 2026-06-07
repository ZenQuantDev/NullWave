using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Owns the SQLite connection and all DB operations.
/// LibraryService calls this; nothing else should.
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly SQLiteConnection _db;
    private bool _disposed;

    public DatabaseService()
    {
        var path = NullWavePaths.DatabasePath;
        _db = new SQLiteConnection(path);
        _db.CreateTable<TrackRecord>();
        Log.Information("[DatabaseService] Opened DB at {Path}", path);
    }

    // ── Core ──────────────────────────────────────────────────────────────

    public List<Track> LoadAll()
    {
        try
        {
            return _db.Table<TrackRecord>()
                      .ToList()
                      .Select(r => r.ToTrack())
                      .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Failed to load tracks");
            return new List<Track>();
        }
    }

    public void Insert(Track track)
    {
        try
        {
            _db.InsertOrReplace(TrackRecord.FromTrack(track));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Insert failed for {Title}", track.Title);
        }
    }

    public void Update(Track track)
    {
        try
        {
            _db.InsertOrReplace(TrackRecord.FromTrack(track));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Update failed for {Title}", track.Title);
        }
    }

    public void Delete(Guid id)
    {
        try
        {
            _db.Delete<TrackRecord>(id.ToString());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Delete failed for {Id}", id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _db.Close();
        _db.Dispose();
        _disposed = true;
    }
}