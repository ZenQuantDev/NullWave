using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Tests;

/// <summary>Small builders so navigator tests stay readable and never touch the database.</summary>
internal static class NavTest
{
    public static List<Track> Music(int count) =>
        Enumerable.Range(0, count)
                  .Select(i => new Track { Title = $"Track {i}", Artist = "Artist" })
                  .ToList();

    public static Track Audiobook(string title, string? album = null, string artist = "Author",
                                  int number = 0, string? path = null) =>
        new()
        {
            Title = title, Album = album, Artist = artist, TrackNumber = number,
            FilePath = path, MediaType = MediaType.Audiobook
        };

    public static PlaybackNavigator Navigator(List<Track> library, Random? rng = null) =>
        new(() => library, () => 0, rng ?? new Random(1));

    /// <summary>NextDouble is fixed and Next() always returns 0, which makes smart shuffle deterministic.</summary>
    public sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;

        public override double NextDouble() => _value;
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
}