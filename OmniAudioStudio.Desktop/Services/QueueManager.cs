using System.Collections.ObjectModel;
using System.IO;
using OmniAudioStudio.Desktop.Models;

namespace OmniAudioStudio.Desktop.Services;

public class QueueManager
{
    private readonly FFmpegLocator _locator;
    private readonly AudioMetadataService _metadataService;
    private CancellationTokenSource? _conversionCts;

    public ObservableCollection<ConversionTask> Tasks { get; } = new();

    public bool IsConverting { get; private set; }

    public int MaxDegreeOfParallelism { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public event Action<string>? LogMessage;
    public event Action<int, int, double>? OverallProgressChanged;
    public event Action<int, int>? QueueFinished;

    public QueueManager(FFmpegLocator locator, AudioMetadataService metadataService)
    {
        _locator = locator;
        _metadataService = metadataService;
    }

    public static string ResolveSafeTargetPath(string sourcePath, string? outputDir, string targetExtension)
    {
        var srcDir = Path.GetDirectoryName(sourcePath) ?? "";
        var destDir = string.IsNullOrWhiteSpace(outputDir) ? srcDir : outputDir;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        var destPath = Path.Combine(destDir, $"{fileNameWithoutExt}{targetExtension}");

        if (string.Equals(Path.GetFullPath(destPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            destPath = Path.Combine(destDir, $"{fileNameWithoutExt}_converted{targetExtension}");
        }

        return destPath;
    }

    public async Task<int> AddPathsAsync(
        IEnumerable<string> paths,
        string targetFormat,
        string? outputDir,
        string bitrate,
        string sampleRate,
        string channels,
        IProgress<string>? statusProgress = null)
    {
        var existingTasksMap = Tasks.ToDictionary(t => Path.GetFullPath(t.SourcePath), StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var filesToProcess = new List<string>();

        // 1. Gather files (expanding directories) in background
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (FormatRegistry.SupportedInputExtensions.Contains(Path.GetExtension(path)))
                    {
                        var full = Path.GetFullPath(path);
                        if (existingTasksMap.TryGetValue(full, out var existingTask))
                        {
                            if (existingTask != null && existingTask.Status != Models.TaskStatus.Converting && existingTask.Status != Models.TaskStatus.Pending)
                            {
                                var profile = FormatRegistry.GetProfile(targetFormat);
                                existingTask.TargetFormat = targetFormat;
                                existingTask.TargetPath = ResolveSafeTargetPath(full, outputDir, profile.Extension);
                                existingTask.Bitrate = bitrate;
                                existingTask.SampleRate = sampleRate;
                                existingTask.Channels = channels;
                                existingTask.Status = Models.TaskStatus.Pending;
                                existingTask.Progress = 0.0;
                                existingTask.ErrorMessage = null;
                            }
                        }
                        else
                        {
                            filesToProcess.Add(full);
                            existingTasksMap[full] = null!;
                        }
                    }
                }
                else if (Directory.Exists(path))
                {
                    try
                    {
                        var found = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                            .Where(f => FormatRegistry.SupportedInputExtensions.Contains(Path.GetExtension(f)));

                        foreach (var f in found)
                        {
                            var full = Path.GetFullPath(f);
                            if (existingTasksMap.TryGetValue(full, out var existingTask))
                            {
                                if (existingTask != null && existingTask.Status != Models.TaskStatus.Converting && existingTask.Status != Models.TaskStatus.Pending)
                                {
                                    var profile = FormatRegistry.GetProfile(targetFormat);
                                    existingTask.TargetFormat = targetFormat;
                                    existingTask.TargetPath = ResolveSafeTargetPath(full, outputDir, profile.Extension);
                                    existingTask.Bitrate = bitrate;
                                    existingTask.SampleRate = sampleRate;
                                    existingTask.Channels = channels;
                                    existingTask.Status = Models.TaskStatus.Pending;
                                    existingTask.Progress = 0.0;
                                    existingTask.ErrorMessage = null;
                                }
                            }
                            else
                            {
                                filesToProcess.Add(full);
                                existingTasksMap[full] = null!;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"Directory scan warning: {ex.Message}");
                    }
                }
            }
        });

        if (filesToProcess.Count == 0 && addedCount == 0)
            return 0;

        statusProgress?.Report($"Processing {filesToProcess.Count} files...");

        // 2. Parse metadata rapidly in parallel using Task.Run
        var tasksCreated = await Task.Run(() =>
        {
            var profile = FormatRegistry.GetProfile(targetFormat);
            var result = new List<ConversionTask>(filesToProcess.Count);

            foreach (var file in filesToProcess)
            {
                var fi = new FileInfo(file);
                var (duration, _, _, _) = _metadataService.ProbeAudio(file);
                var ext = fi.Extension;
                var targetPath = ResolveSafeTargetPath(file, outputDir, profile.Extension);

                var task = new ConversionTask
                {
                    SourcePath = file,
                    FileName = fi.Name,
                    FileSize = fi.Exists ? fi.Length : 0,
                    Duration = duration,
                    SourceFormat = FormatRegistry.DetectSourceFormatName(ext),
                    TargetFormat = targetFormat,
                    TargetPath = targetPath,
                    Bitrate = bitrate,
                    SampleRate = sampleRate,
                    Channels = channels
                };
                result.Add(task);
            }
            return result;
        });

        // 3. Add to UI collection safely
        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var task in tasksCreated)
                {
                    Tasks.Add(task);
                    addedCount++;
                }
            });
        }
        else
        {
            foreach (var task in tasksCreated)
            {
                Tasks.Add(task);
                addedCount++;
            }
        }

        LogMessage?.Invoke($"Added {addedCount} files to queue.");
        EmitProgress();
        return addedCount;
    }

    public void RemoveTask(ConversionTask task)
    {
        if (IsConverting && task.Status == Models.TaskStatus.Converting)
            return;

        Tasks.Remove(task);
        EmitProgress();
    }

    public void Clear()
    {
        if (IsConverting) return;
        Tasks.Clear();
        EmitProgress();
    }

    public void UpdatePendingTasksFormat(string targetFormat, string? outputDir, string bitrate, string sampleRate, string channels)
    {
        var profile = FormatRegistry.GetProfile(targetFormat);
        foreach (var task in Tasks)
        {
            if (task.Status != Models.TaskStatus.Converting)
            {
                task.TargetFormat = targetFormat;
                task.Bitrate = bitrate;
                task.SampleRate = sampleRate;
                task.Channels = channels;
                task.TargetPath = ResolveSafeTargetPath(task.SourcePath, outputDir, profile.Extension);
                if (task.Status == Models.TaskStatus.Completed)
                {
                    task.Status = Models.TaskStatus.Pending;
                    task.Progress = 0.0;
                    task.ErrorMessage = null;
                    task.ActualFinalFileSize = null;
                }
            }
        }
        EmitProgress();
    }

    public async Task StartConversionAsync()
    {
        if (IsConverting || Tasks.Count == 0)
            return;

        if (string.IsNullOrEmpty(_locator.FFmpegPath) || !File.Exists(_locator.FFmpegPath))
        {
            LogMessage?.Invoke("Error: FFmpeg binary not found!");
            return;
        }

        var pendingTasks = Tasks
            .Where(t => t.Status is Models.TaskStatus.Pending or Models.TaskStatus.Failed or Models.TaskStatus.Cancelled)
            .ToList();

        if (pendingTasks.Count == 0 && Tasks.Count > 0)
        {
            foreach (var t in Tasks)
            {
                t.Status = Models.TaskStatus.Pending;
                t.Progress = 0.0;
                t.ErrorMessage = null;
            }
            pendingTasks = Tasks.ToList();
            EmitProgress();
        }

        if (pendingTasks.Count == 0)
        {
            LogMessage?.Invoke("Queue is empty. Please add audio files to convert.");
            return;
        }

        IsConverting = true;
        _conversionCts = new CancellationTokenSource();
        var token = _conversionCts.Token;

        var engine = new ConversionEngine(_locator.FFmpegPath);
        var semaphore = new SemaphoreSlim(MaxDegreeOfParallelism, MaxDegreeOfParallelism);

        LogMessage?.Invoke($"Starting batch conversion of {pendingTasks.Count} tasks ({MaxDegreeOfParallelism} concurrent workers)...");

        int completed = Tasks.Count(t => t.Status == Models.TaskStatus.Completed);
        int total = Tasks.Count;

        try
        {
            var runningTasks = pendingTasks.Select(async task =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    if (token.IsCancellationRequested)
                    {
                        task.Status = Models.TaskStatus.Cancelled;
                        return;
                    }

                    LogMessage?.Invoke($"Converting: {task.FileName} → {task.TargetFormat}");
                    var progress = new Progress<double>(_ => EmitProgress());

                    var success = await engine.ConvertAsync(task, progress, token);
                    if (success)
                    {
                        LogMessage?.Invoke($"Finished: {task.FileName}");
                    }
                    else if (!token.IsCancellationRequested)
                    {
                        LogMessage?.Invoke($"Failed: {task.FileName} ({task.ErrorMessage})");
                    }
                }
                finally
                {
                    semaphore.Release();
                    EmitProgress();
                }
            });

            await Task.WhenAll(runningTasks);
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke("Conversion batch cancelled by user.");
        }
        finally
        {
            IsConverting = false;
            int successCount = Tasks.Count(t => t.Status == Models.TaskStatus.Completed);
            int failedCount = Tasks.Count(t => t.Status == Models.TaskStatus.Failed);
            LogMessage?.Invoke($"Batch finished. Completed: {successCount}, Failed: {failedCount}.");
            QueueFinished?.Invoke(successCount, failedCount);
            EmitProgress();
        }
    }

    public void CancelConversion()
    {
        if (!IsConverting || _conversionCts == null)
            return;

        LogMessage?.Invoke("Cancelling active conversion...");
        _conversionCts.Cancel();
    }

    private void EmitProgress()
    {
        int completed = Tasks.Count(t => t.Status == Models.TaskStatus.Completed);
        int total = Tasks.Count;
        double overallPct = 0;

        if (total > 0)
        {
            double sum = Tasks.Sum(t => t.Status == Models.TaskStatus.Completed ? 100.0 : (t.Status == Models.TaskStatus.Converting ? t.Progress : 0.0));
            overallPct = Math.Round(sum / total, 1);
        }

        OverallProgressChanged?.Invoke(completed, total, overallPct);
    }
}
