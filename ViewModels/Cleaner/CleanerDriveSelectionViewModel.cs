using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels.Cleaner
{
    public partial class CleanerDriveSelectionViewModel : ObservableObject
    {
        private readonly CleanerDriveService _driveService;
        private readonly CleanerStateStore _stateStore;
        private bool _isUpdatingDriveSelection;

        public ObservableCollection<CleanerDriveOption> DriveOptions { get; } = new();

        public bool HasDriveOptions => DriveOptions.Count > 0;

        public string SelectedDriveSummaryText
        {
            get
            {
                List<CleanerDriveOption> selected = GetSelectedDriveOptions();
                if (selected.Count == 0)
                {
                    return "未选择磁盘";
                }

                return $"已选 {selected.Count} 个磁盘 · {string.Join(" / ", selected.Select(option => option.Name))}";
            }
        }

        public string DriveSelectionHintText => "深度扫描会按这里选中的磁盘做空间分析；系统与应用规则仍按真实安装位置识别。";

        public CleanerDriveSelectionViewModel(CleanerDriveService driveService, CleanerStateStore stateStore)
        {
            _driveService = driveService;
            _stateStore = stateStore;
        }

        public async Task InitializeAsync()
        {
            await ReloadDriveOptionsAsync();
        }

        [RelayCommand]
        private Task SelectAllDrives()
        {
            ApplyDriveSelection(option => true);
            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task UseSystemDriveOnly()
        {
            ApplyDriveSelection(option => option.IsSystemDrive);
            return Task.CompletedTask;
        }

        private async Task ReloadDriveOptionsAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            HashSet<string> selectedRoots = preferences.SelectedDriveRoots.ToHashSet(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<CleanerDriveOption> availableDrives = _driveService.GetAvailableDrives();

            _isUpdatingDriveSelection = true;
            try
            {
                foreach (CleanerDriveOption drive in DriveOptions)
                {
                    drive.PropertyChanged -= DriveOption_PropertyChanged;
                }

                DriveOptions.Clear();
                foreach (CleanerDriveOption drive in availableDrives)
                {
                    bool shouldSelect = selectedRoots.Count > 0
                        ? selectedRoots.Contains(NormalizePath(drive.RootPath))
                        : drive.IsSystemDrive;

                    drive.IsSelected = shouldSelect;
                    drive.PropertyChanged += DriveOption_PropertyChanged;
                    DriveOptions.Add(drive);
                }

                if (DriveOptions.Count > 0 && DriveOptions.All(option => !option.IsSelected))
                {
                    CleanerDriveOption fallback = DriveOptions.FirstOrDefault(option => option.IsSystemDrive) ?? DriveOptions[0];
                    fallback.IsSelected = true;
                }
            }
            finally
            {
                _isUpdatingDriveSelection = false;
            }

            await PersistDriveSelectionAsync();
            OnPropertyChanged(nameof(HasDriveOptions));
        }

        private void ApplyDriveSelection(Func<CleanerDriveOption, bool> selector)
        {
            if (DriveOptions.Count == 0)
            {
                return;
            }

            _isUpdatingDriveSelection = true;
            try
            {
                foreach (CleanerDriveOption drive in DriveOptions)
                {
                    drive.IsSelected = selector(drive);
                }

                if (DriveOptions.All(option => !option.IsSelected))
                {
                    CleanerDriveOption fallback = DriveOptions.FirstOrDefault(option => option.IsSystemDrive) ?? DriveOptions[0];
                    fallback.IsSelected = true;
                }
            }
            finally
            {
                _isUpdatingDriveSelection = false;
            }

            _ = PersistDriveSelectionAsync();
        }

        private async void DriveOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CleanerDriveOption.IsSelected) || _isUpdatingDriveSelection)
            {
                return;
            }

            if (sender is CleanerDriveOption changedOption && DriveOptions.All(option => !option.IsSelected))
            {
                _isUpdatingDriveSelection = true;
                try
                {
                    changedOption.IsSelected = true;
                }
                finally
                {
                    _isUpdatingDriveSelection = false;
                }
                return;
            }

            await PersistDriveSelectionAsync();
        }

        public List<CleanerDriveOption> GetSelectedDriveOptions()
        {
            return DriveOptions
                .Where(option => option.IsSelected)
                .OrderByDescending(option => option.IsSystemDrive)
                .ToList();
        }

        public List<string> GetSelectedDriveRoots()
        {
            return GetSelectedDriveOptions().Select(d => NormalizePath(d.RootPath)).ToList();
        }

        private async Task PersistDriveSelectionAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            preferences.SelectedDriveRoots = GetSelectedDriveRoots();
            await _stateStore.SavePreferencesAsync(preferences);

            OnPropertyChanged(nameof(SelectedDriveSummaryText));
        }

        private static string NormalizePath(string path)
        {
            return path.TrimEnd('\\', '/') + "\\";
        }
    }
}
