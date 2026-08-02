    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using Avalonia;
    using Avalonia.Controls.ApplicationLifetimes;
    using NullWave.Helpers;
    using NullWave.Models;
    using NullWave.Services;
    using NullWave.ViewModels.Base;
    using Serilog;

    namespace NullWave.ViewModels;

    public class PlaylistViewModel : ViewModelBase
    {
        private readonly PlaylistService _playlists;
        private Playlist? _selectedPlaylist;
        private string _renameText = string.Empty;
        private bool _isRenaming;

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSearchQuery)); RefreshFilteredTracks(); }
        }
        public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery);

        private SortField _currentSort = SortField.DateAdded;
        public SortField CurrentSort
        {
            get => _currentSort;
            set { _currentSort = value; OnPropertyChanged(); RefreshSortIndicators(); RefreshFilteredTracks(); }
        }

        private bool _sortAscending = true;
        public bool SortAscending
        {
            get => _sortAscending;
            set { _sortAscending = value; OnPropertyChanged(); RefreshFilteredTracks(); }
        }

        public Array SortOptions => Enum.GetValues(typeof(SortField));

        public bool IsSortedByTitle => CurrentSort == SortField.Title;
        public bool IsSortedByArtist => CurrentSort == SortField.Artist;
        public bool IsSortedBySource => CurrentSort == SortField.Source;
        public bool IsSortedByDate => CurrentSort == SortField.DateAdded;

        public BulkObservableCollection<Track> FilteredTracks { get; } = new();
        public string ResultCountLabel => FilteredTracks.Count == 1 ? "1 track" : $"{FilteredTracks.Count} tracks";

        public ObservableCollection<Playlist> Playlists { get; } = new();

        public ICommand CreatePlaylistCommand { get; }
        public ICommand CreateFolderCommand { get; }
        public ICommand RemovePlaylistCommand { get; }
        public ICommand AddToPlaylistCommand { get; }
        public ICommand RemoveFromPlaylistCommand { get; }
        public ICommand StartRenameCommand { get; }
        public ICommand ConfirmRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand PinCommand { get; }
        public ICommand UnpinCommand { get; }
        public ICommand ClearSearchTextCommand { get; }
        public ICommand SortByTitleCommand { get; }
        public ICommand SortByArtistCommand { get; }
        public ICommand SortBySourceCommand { get; }
        public ICommand SortByDateCommand { get; }
        public ICommand ToggleSortDirectionCommand { get; }
        public ICommand PlayAllCommand { get; }

        public event Action<Playlist>? PinRequested;
        public event Action<Playlist>? UnpinRequested;
        public event Action? PlaylistsChanged;
        public event Action<Playlist>? PlayAllRequested;

        public Playlist? SelectedPlaylist
        {
            get => _selectedPlaylist;
            set { _selectedPlaylist = value; OnPropertyChanged(); RefreshFilteredTracks(); }
        }

        public string RenameText
        {
            get => _renameText;
            set { _renameText = value; OnPropertyChanged(); }
        }

        public bool IsRenaming
        {
            get => _isRenaming;
            set { _isRenaming = value; OnPropertyChanged(); }
        }

        private NavigationViewModel? _nav;

        public void AttachNavigation(NavigationViewModel nav)
        {
            _nav = nav;
            RefreshPinnedState();
        }

        private void RefreshPinnedState()
        {
            if (_nav == null) return;
            foreach (var p in Playlists)
                p.IsPinned = _nav.IsPlaylistPinned(p.Id);
        }

        public PlaylistViewModel(PlaylistService playlists)
        {
            _playlists = playlists;
            CreatePlaylistCommand = new RelayCommand(async () => await CreatePlaylistAsync());
            CreateFolderCommand = new RelayCommand(async () => await CreateFolderAsync());
            RemovePlaylistCommand = new RelayCommand(RemovePlaylist);
            AddToPlaylistCommand = new RelayCommand<Track>(AddToPlaylist);
            RemoveFromPlaylistCommand = new RelayCommand<Track>(RemoveFromPlaylist);
            StartRenameCommand = new RelayCommand(StartRename);
            ConfirmRenameCommand = new RelayCommand(ConfirmRename);
            CancelRenameCommand = new RelayCommand(() => IsRenaming = false);
            
            PinCommand = new RelayCommand(() =>
            {
                if (SelectedPlaylist != null) PinRequested?.Invoke(SelectedPlaylist);
            });
            
            UnpinCommand = new RelayCommand(() =>
            {
                if (SelectedPlaylist != null) UnpinRequested?.Invoke(SelectedPlaylist);
            });

            ClearSearchTextCommand = new RelayCommand(() => SearchQuery = string.Empty);
            SortByTitleCommand = new RelayCommand(() => SetSort(SortField.Title));
            SortByArtistCommand = new RelayCommand(() => SetSort(SortField.Artist));
            SortBySourceCommand = new RelayCommand(() => SetSort(SortField.Source));
            SortByDateCommand = new RelayCommand(() => SetSort(SortField.DateAdded));
            ToggleSortDirectionCommand = new RelayCommand(() => SortAscending = !SortAscending);

            PlayAllCommand = new RelayCommand(() =>
            {
                if (SelectedPlaylist != null) PlayAllRequested?.Invoke(SelectedPlaylist);
            });

            Refresh();
        }

        public void Refresh()
        {
            var selectedId = SelectedPlaylist?.Id;

            Playlists.Clear();
            foreach (var playlist in _playlists.GetAll())
                Playlists.Add(playlist);

            SelectedPlaylist = selectedId.HasValue
                ? Playlists.FirstOrDefault(p => p.Id == selectedId.Value)
                : null;

            RefreshPinnedState();
        }

        private async Task CreatePlaylistAsync()
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;
            if (window == null) return;

            var dialog = new Views.CreatePlaylistDialog();
            var name = await dialog.ShowDialog<string?>(window);

            if (string.IsNullOrWhiteSpace(name)) return;

            if (_playlists.NameExists(name))
                name = $"{name} ({Guid.NewGuid().ToString()[..4]})";

            var playlist = _playlists.Create(name);
            Playlists.Add(playlist);
            SelectedPlaylist = playlist;
            Log.Information("[Playlist] Created: {Name}", name);
            ToastService.Instance.Show($"Playlist '{playlist.Name}' created.", ToastType.Success);
            PlaylistsChanged?.Invoke();
        }

        private async Task CreateFolderAsync()
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;
            if (window == null) return;

            var dialog = new Views.CreateFolderDialog();
            var name = await dialog.ShowDialog<string?>(window);

            if (string.IsNullOrWhiteSpace(name)) return;

            var folder = _playlists.CreateFolder(name);
            Log.Information("[PlaylistFolder] Created: {Name}", name);
            PlaylistsChanged?.Invoke();
        }

        private void RemovePlaylist()
        {
            if (SelectedPlaylist == null) return;
            _ = RemovePlaylistAsync(SelectedPlaylist);
        }

        private async Task RemovePlaylistAsync(Playlist target)
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;
            if (window == null) return;

            var dialog = new Views.ConfirmDialog(
                "Delete Playlist?",
                $"Delete '{target.Name}'? This can't be undone. Tracks in your library are not affected.");
            var confirmed = await dialog.ShowDialog<bool>(window);
            if (!confirmed) return;

            _playlists.Remove(target.Id);
            Playlists.Remove(target);
            if (SelectedPlaylist?.Id == target.Id) SelectedPlaylist = null;

            ToastService.Instance.Show($"Playlist '{target.Name}' deleted.", ToastType.Info);
            Log.Information("[Playlist] Removed: {Name}", target.Name);
            PlaylistsChanged?.Invoke();
        }

        private void StartRename()
        {
            if (SelectedPlaylist == null) return;
            RenameText = SelectedPlaylist.Name;
            IsRenaming = true;
        }

        private void ConfirmRename()
        {
            if (SelectedPlaylist == null) return;

            var trimmed = RenameText.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                ToastService.Instance.Show("Playlist name can't be empty.", ToastType.Warning);
                return;
            }

            var oldName = SelectedPlaylist.Name;
            _playlists.Rename(SelectedPlaylist.Id, trimmed);
            
            Refresh();
            SelectedPlaylist = Playlists.FirstOrDefault(p => p.Name == trimmed) ?? SelectedPlaylist;

            ToastService.Instance.Show($"Renamed '{oldName}' to '{trimmed}'.", ToastType.Success);
            IsRenaming = false;
            PlaylistsChanged?.Invoke();
        }

        private void AddToPlaylist(Track? track)
        {
            if (track == null || SelectedPlaylist == null) return;

            var added = _playlists.AddTrack(SelectedPlaylist.Id, track);
            if (added)
                ToastService.Instance.Show($"Added '{track.Title}' to '{SelectedPlaylist.Name}'.", ToastType.Success);
            else
                ToastService.Instance.Show($"'{track.Title}' is already in '{SelectedPlaylist.Name}'.", ToastType.Info);

            RefreshFilteredTracks();
        }

        private void RemoveFromPlaylist(Track? track)
        {
            if (track == null || SelectedPlaylist == null) return;

            var removed = _playlists.RemoveTrack(SelectedPlaylist.Id, track.Id);
            if (removed)
                ToastService.Instance.Show($"Removed '{track.Title}' from '{SelectedPlaylist.Name}'.", ToastType.Info);

            RefreshFilteredTracks();
        }

        public void SelectById(Guid playlistId)
        {
            SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == playlistId);
        }

        private void SetSort(SortField field)
        {
            if (CurrentSort == field) SortAscending = !SortAscending;
            else { CurrentSort = field; SortAscending = true; }
        }

        private void RefreshSortIndicators()
        {
            OnPropertyChanged(nameof(IsSortedByTitle));
            OnPropertyChanged(nameof(IsSortedByArtist));
            OnPropertyChanged(nameof(IsSortedBySource));
            OnPropertyChanged(nameof(IsSortedByDate));
        }

        private void RefreshFilteredTracks()
        {
            if (SelectedPlaylist == null)
            {
                FilteredTracks.Clear();
                OnPropertyChanged(nameof(ResultCountLabel));
                return;
            }

            IEnumerable<Track> tracks = SelectedPlaylist.Tracks;

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.ToLowerInvariant();
                tracks = tracks.Where(t =>
                    t.Title.ToLowerInvariant().Contains(q) ||
                    t.Artist.ToLowerInvariant().Contains(q));
            }

            IEnumerable<Track> sorted = CurrentSort switch
            {
                SortField.Title  => tracks.OrderBy(t => t.Title).ThenBy(t => t.Artist),
                SortField.Artist => tracks.OrderBy(t => t.Artist).ThenBy(t => t.Title),
                SortField.Source => tracks.OrderBy(t => t.Source).ThenBy(t => t.Title),
                _                => tracks.OrderBy(t => t.DateAdded).ThenBy(t => t.Title),
            };

            FilteredTracks.ReplaceAll(SortAscending ? sorted : sorted.Reverse());
            OnPropertyChanged(nameof(ResultCountLabel));
        }
    }