using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniAudioStudio.Desktop.Models;

public partial class ConversionTask : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }

    private TimeSpan _duration;
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(FormattedDuration));
                OnPropertyChanged(nameof(FormattedDetails));
                OnPropertyChanged(nameof(EstimatedOrActualTargetSize));
            }
        }
    }

    public required string SourceFormat { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDetails))]
    [NotifyPropertyChangedFor(nameof(EstimatedOrActualTargetSize))]
    private string _targetFormat = "MP3";

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedOrActualTargetSize))]
    private string _bitrate = "320 kbps";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedOrActualTargetSize))]
    private string _sampleRate = "Original";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedOrActualTargetSize))]
    private string _channels = "Original";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsConverting))]
    [NotifyPropertyChangedFor(nameof(EstimatedOrActualTargetSize))]
    private TaskStatus _status = TaskStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double _progress = 0.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDetails))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedOrActualTargetSize))]
    private long? _actualFinalFileSize;

    public string StatusText => Status switch
    {
        TaskStatus.Pending => "Pending",
        TaskStatus.Converting => $"{Progress:0.#}%",
        TaskStatus.Completed => "Completed",
        TaskStatus.Failed => "Failed",
        TaskStatus.Cancelled => "Cancelled",
        _ => Status.ToString()
    };

    public bool IsConverting => Status == TaskStatus.Converting;

    public string FormattedFileSize => FormatBytes(FileSize);

    public string FormattedDuration => Duration > TimeSpan.Zero
        ? $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}"
        : string.Empty;

    public string EstimatedOrActualTargetSize
    {
        get
        {
            if (Status == TaskStatus.Completed && ActualFinalFileSize.HasValue && ActualFinalFileSize.Value > 0)
            {
                return FormatBytes(ActualFinalFileSize.Value);
            }

            if (Status == TaskStatus.Completed && !string.IsNullOrEmpty(TargetPath) && File.Exists(TargetPath))
            {
                try
                {
                    var len = new FileInfo(TargetPath).Length;
                    return FormatBytes(len);
                }
                catch { }
            }

            return EstimateTargetSize();
        }
    }

    private string EstimateTargetSize()
    {
        var targetFmt = TargetFormat.ToUpperInvariant();
        double totalSeconds = Duration.TotalSeconds;

        // 1. Lossy formats (MP3, AAC, OGG, OPUS, WMA)
        if (targetFmt is "MP3" or "AAC" or "OGG" or "OPUS" or "WMA")
        {
            int kbps = 320;
            var match = Regex.Match(Bitrate, @"(\d+)");
            if (match.Success)
            {
                kbps = int.Parse(match.Groups[1].Value);
            }

            if (targetFmt == "OPUS" && kbps > 256) kbps = 256;
            if (targetFmt == "OGG" && Channels.Contains("Mono", StringComparison.OrdinalIgnoreCase) && kbps > 192) kbps = 192;

            if (totalSeconds > 0)
            {
                long estimatedBytes = (long)(totalSeconds * (kbps * 1000.0 / 8.0)) + 8192;
                return $"~{FormatBytes(estimatedBytes)}";
            }
            else if (FileSize > 0)
            {
                double ratio = kbps switch
                {
                    >= 320 => 0.35,
                    >= 256 => 0.28,
                    >= 192 => 0.22,
                    >= 128 => 0.15,
                    _ => 0.10
                };
                return $"~{FormatBytes((long)(FileSize * ratio))}";
            }
        }

        // 2. Uncompressed Lossless (WAV, AIFF)
        if (targetFmt is "WAV" or "AIFF")
        {
            int sampleRate = SampleRate.Contains("48000") ? 48000 : (SampleRate.Contains("96000") ? 96000 : 44100);
            int ch = Channels.Contains("Mono", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

            if (totalSeconds > 0)
            {
                long bytes = (long)(totalSeconds * sampleRate * ch * 2) + 44;
                return $"~{FormatBytes(bytes)}";
            }
            else if (FileSize > 0)
            {
                return $"~{FormatBytes((long)(FileSize * 1.5))}";
            }
        }

        // 3. Compressed Lossless (FLAC, ALAC)
        if (targetFmt is "FLAC" or "ALAC")
        {
            if (string.Equals(SourceFormat, targetFmt, StringComparison.OrdinalIgnoreCase) && FileSize > 0)
            {
                return $"~{FormatBytes(FileSize)}";
            }

            int sampleRate = SampleRate.Contains("48000") ? 48000 : (SampleRate.Contains("96000") ? 96000 : 44100);
            int ch = Channels.Contains("Mono", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

            if (totalSeconds > 0)
            {
                long uncompressedBytes = (long)(totalSeconds * sampleRate * ch * 2);
                long flacBytes = (long)(uncompressedBytes * 0.58);
                return $"~{FormatBytes(flacBytes)}";
            }
            else if (FileSize > 0)
            {
                return $"~{FormatBytes(FileSize)}";
            }
        }

        return $"~{FormatBytes(FileSize)}";
    }

    public string FormattedDetails
    {
        get
        {
            if (Status == TaskStatus.Failed && !string.IsNullOrWhiteSpace(ErrorMessage))
                return ErrorMessage;

            var parts = new List<string> { SourceFormat, FormattedFileSize };
            if (!string.IsNullOrEmpty(FormattedDuration))
                parts.Add(FormattedDuration);
            parts.Add($"→ {TargetFormat}");
            return string.Join("  ·  ", parts);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }
}
