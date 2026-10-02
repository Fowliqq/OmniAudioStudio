using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OmniAudioStudio.Desktop.Models;
using OmniAudioStudio.Desktop.Services;
using Wpf.Ui.Appearance;

namespace OmniAudioStudio.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FFmpegLocator _locator;
    private readonly QueueManager _queueManager;

    public ObservableCollection<ConversionTask> Tasks => _queueManager.Tasks;
    public ObservableCollection<string> Logs { get; } = new();

    public IReadOnlyList<string> TargetFormats { get; } = FormatRegistry.AvailableFormats;
    public IReadOnlyList<string> Bitrates { get; } = new[]
    {
        "320 kbps (Extreme)",
        "256 kbps (High)",
        "192 kbps (Standard)",
        "128 kbps (Compact)",
        "96 kbps (Voice)"
    };
    public IReadOnlyList<string> SampleRates { get; } = new[]
    {
        "Original",
        "44100 Hz (CD)",
        "48000 Hz (Studio)",
        "96000 Hz (Hi-Res)"
    };
    public IReadOnlyList<string> Channels { get; } = new[]
    {
        "Original",
        "Stereo (2.0)",
        "Mono (1.0)"
    };
    public IReadOnlyList<string> ConcurrencyOptions { get; } = new[]
    {
        "2 threads",
        "4 threads (Recommended)",
        "8 threads",
        "1 thread (Sequential)"
    };

    [ObservableProperty]
    private string _selectedTargetFormat = "MP3";

    [ObservableProperty]
    private string _selectedBitrate = "320 kbps (Extreme)";

    [ObservableProperty]
    private bool _isBitrateEnabled = true;

    [ObservableProperty]
    private string _selectedSampleRate = "Original";

    [ObservableProperty]
    private string _selectedChannels = "Original";

    [ObservableProperty]
    private string _selectedConcurrency = "4 threads (Recommended)";

    [ObservableProperty]
    private string? _customOutputDir;

    [ObservableProperty]
    private string? _detectedSourceBadge;

    [ObservableProperty]
    private string _ffmpegStatusText = "● Checking...";

    [ObservableProperty]
    private SolidColorBrush _ffmpegStatusBrush = new(Color.FromRgb(148, 163, 184));

    [ObservableProperty]
    private SolidColorBrush _ffmpegStatusBgBrush = new(Color.FromArgb(24, 148, 163, 184));

    [ObservableProperty]
    private SolidColorBrush _ffmpegStatusBorderBrush = new(Color.FromArgb(50, 148, 163, 184));

    [ObservableProperty]
    private bool _isConverting = false;

    [ObservableProperty]
    private bool _hasCompletedTasks = false;

    [ObservableProperty]
    private double _overallProgress = 0.0;

    [ObservableProperty]
    private string _overallProgressText = "0 / 0 files (0%)";

    [ObservableProperty]
    private int _tasksCount = 0;

    [ObservableProperty]
    private ApplicationTheme _currentTheme = ApplicationTheme.Dark;

    public MainViewModel(FFmpegLocator locator, QueueManager queueManager)
    {
        _locator = locator;
        _queueManager = queueManager;

        _queueManager.LogMessage += OnLogMessage;
        _queueManager.OverallProgressChanged += OnOverallProgressChanged;
        _queueManager.QueueFinished += OnQueueFinished;

        InitFFmpegStatus();
    }

    private void InitFFmpegStatus()
    {
        if (_locator.IsReady)
        {
            FfmpegStatusText = "● FFmpeg Ready";
            FfmpegStatusBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));        // #34D399 Emerald-400
            FfmpegStatusBgBrush = new SolidColorBrush(Color.FromArgb(28, 16, 185, 129)); // #1C10B981
            FfmpegStatusBorderBrush = new SolidColorBrush(Color.FromArgb(64, 16, 185, 129)); // #4010B981
            OnLogMessage($"FFmpeg detected: {_locator.FFmpegPath}");
            if (!string.IsNullOrEmpty(_locator.VersionBanner))
            {
                OnLogMessage(_locator.VersionBanner);
            }
        }
        else
        {
            FfmpegStatusText = "● FFmpeg Missing";
            FfmpegStatusBrush = new SolidColorBrush(Color.FromRgb(251, 113, 133));       // #FB7185 Rose-400
            FfmpegStatusBgBrush = new SolidColorBrush(Color.FromArgb(28, 244, 63, 94));  // #1CF43F5E
            FfmpegStatusBorderBrush = new SolidColorBrush(Color.FromArgb(64, 244, 63, 94)); // #40F43F5E
            OnLogMessage("Warning: FFmpeg binary not found. Please place ffmpeg.exe in 'bin' folder or system PATH.");
        }
    }

    partial void OnSelectedTargetFormatChanged(string value)
    {
        var profile = FormatRegistry.GetProfile(value);
        IsBitrateEnabled = !profile.IsLossless;
        UpdatePendingTasks();
    }

    partial void OnSelectedBitrateChanged(string value) => UpdatePendingTasks();
    partial void OnSelectedSampleRateChanged(string value) => UpdatePendingTasks();
    partial void OnSelectedChannelsChanged(string value) => UpdatePendingTasks();
    partial void OnCustomOutputDirChanged(string? value) => UpdatePendingTasks();

    partial void OnSelectedConcurrencyChanged(string value)
    {
        int threads = value switch
        {
            var s when s.StartsWith("1") => 1,
            var s when s.StartsWith("2") => 2,
            var s when s.StartsWith("8") => 8,
            _ => 4
        };
        _queueManager.MaxDegreeOfParallelism = threads;
    }

    private void UpdatePendingTasks()
    {
        _queueManager.UpdatePendingTasksFormat(
            SelectedTargetFormat,
            CustomOutputDir,
            SelectedBitrate,
            SelectedSampleRate,
            SelectedChannels);
    }

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;

        await _queueManager.AddPathsAsync(
            list,
            SelectedTargetFormat,
            CustomOutputDir,
            SelectedBitrate,
            SelectedSampleRate,
            SelectedChannels);

        TasksCount = Tasks.Count;
        if (Tasks.Count > 0)
        {
            var primaryExt = Tasks
                .Select(t => Path.GetExtension(t.SourcePath).ToLowerInvariant())
                .Where(e => FormatRegistry.SupportedInputExtensions.Contains(e))
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            if (!string.IsNullOrEmpty(primaryExt))
            {
                var primaryFormat = FormatRegistry.DetectSourceFormatName(primaryExt);
                DetectedSourceBadge = $"{primaryFormat} ({Tasks.Count} file{(Tasks.Count == 1 ? "" : "s")})";
            }
        }
        else
        {
            DetectedSourceBadge = null;
        }
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var extFilters = string.Join(";", FormatRegistry.SupportedInputExtensions.Select(e => $"*{e}"));
        var dialog = new OpenFileDialog
        {
            Title = "Select Music Files",
            Filter = $"Audio Files ({extFilters})|{extFilters}|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder with Music",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            await AddPathsAsync(new[] { dialog.FolderName });
        }
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (IsConverting) return;
        _queueManager.Clear();
        DetectedSourceBadge = null;
        TasksCount = 0;
        OverallProgress = 0.0;
        OverallProgressText = "0 / 0 files (0%)";
        HasCompletedTasks = false;
    }

    [RelayCommand]
    private void RemoveTask(ConversionTask task)
    {
        _queueManager.RemoveTask(task);
        TasksCount = Tasks.Count;
        if (Tasks.Count == 0)
        {
            DetectedSourceBadge = null;
            HasCompletedTasks = false;
        }
    }

    [RelayCommand]
    private async Task StartConversionAsync()
    {
        if (IsConverting) return;
        if (Tasks.Count == 0)
        {
            OnLogMessage("Queue is empty. Please add audio files to convert.");
            return;
        }
        IsConverting = true;
        HasCompletedTasks = false;
        await _queueManager.StartConversionAsync();
        IsConverting = false;
    }

    [RelayCommand]
    private void CancelConversion()
    {
        _queueManager.CancelConversion();
    }

    [RelayCommand]
    private void BrowseOutputDir()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Output Folder for Converted Files"
        };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            CustomOutputDir = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void ResetOutputDir()
    {
        CustomOutputDir = null;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        string? targetDir = CustomOutputDir;
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            var firstTask = Tasks.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.TargetPath));
            if (firstTask != null)
            {
                targetDir = Path.GetDirectoryName(firstTask.TargetPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error opening folder: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var newTheme = CurrentTheme == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(newTheme);
        CurrentTheme = newTheme;
    }

    private void OnLogMessage(string msg)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Logs.Add($"[{timestamp}] {msg}");
            if (Logs.Count > 200)
            {
                Logs.RemoveAt(0);
            }
        });
    }

    private void OnOverallProgressChanged(int completed, int total, double percent)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            TasksCount = total;
            OverallProgress = percent;
            OverallProgressText = $"{completed} / {total} files ({percent:0.#}%)";
        });
    }

    private void OnQueueFinished(int completed, int failed)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConverting = false;
            if (completed > 0)
            {
                HasCompletedTasks = true;
            }
        });
    }
}
