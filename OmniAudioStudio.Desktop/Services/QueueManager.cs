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

    private int _maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Clamp(value, 1, 8);
    }

    public event Action<string>? LogMessage;
    public event Action<int, int, double>? OverallProgressChanged;
    public event Action<int, int>? QueueFinished;

    public QueueManager(FFmpegLocator locator, AudioMetadataService metadataService)
    {
        _locator = locator;
        _metadataService = metadataService;
    }

    public static string ResolveSafeTargetPath(
        string sourcePath,
        string? outputDir,
        string targetExtension,
        IEnumerable<string>? reservedTargetPaths = null)
    {
        var srcDir = Path.GetDirectoryName(sourcePath) ?? "";
        var destDir = string.IsNullOrWhiteSpace(outputDir) ? srcDir : outputDir;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = targetExtension.StartsWith('.') ? targetExtension : $".{targetExtension}";
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (reservedTargetPaths != null)
        {
            foreach (var path in reservedTargetPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                reservedPaths.Add(Path.GetFullPath(path));
            }
        }

        var destPath = Path.Combine(destDir, $"{fileNameWithoutExt}{extension}");
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var isSourcePath = string.Equals(Path.GetFullPath(destPath), sourceFullPath, StringComparison.OrdinalIgnoreCase);

        if (isSourcePath || File.Exists(destPath) || Directory.Exists(destPath) || reservedPaths.Contains(Path.GetFullPath(destPath)))
        {
            destPath = Path.Combine(destDir, $"{fileNameWithoutExt}_converted{extension}");
        }

        var suffix = 2;
        while (File.Exists(destPath) || Directory.Exists(destPath) || reservedPaths.Contains(Path.GetFullPath(destPath)))
        {
            destPath = Path.Combine(destDir, $"{fileNameWithoutExt}_converted_{suffix++}{extension}");
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
        var existingTasksToReset = new List<ConversionTask>();
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
                                existingTasksToReset.Add(existingTask);
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
                        var found = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                            {
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = true,
                                AttributesToSkip = FileAttributes.ReparsePoint
                            })
                            .Where(f => FormatRegistry.SupportedInputExtensions.Contains(Path.GetExtension(f)));

                        foreach (var f in found)
                        {
                            var full = Path.GetFullPath(f);
                            if (existingTasksMap.TryGetValue(full, out var existingTask))
                            {
                                if (existingTask != null && existingTask.Status != Models.TaskStatus.Converting && existingTask.Status != Models.TaskStatus.Pending)
                                {
                                    existingTasksToReset.Add(existingTask);
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

        if (filesToProcess.Count == 0 && existingTasksToReset.Count == 0)
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
                var (duration, _, sourceSampleRate, sourceChannels) = _metadataService.ProbeAudio(file);
                var ext = fi.Extension;
                var task = new ConversionTask
                {
                    SourcePath = file,
                    FileName = fi.Name,
                    FileSize = fi.Exists ? fi.Length : 0,
                    Duration = duration,
                    SourceSampleRate = sourceSampleRate,
                    SourceChannels = sourceChannels,
                    SourceFormat = FormatRegistry.DetectSourceFormatName(ext),
                    TargetFormat = targetFormat,
                    TargetPath = string.Empty,
                    Bitrate = bitrate,
                    SampleRate = sampleRate,
                    Channels = channels
                };
                result.Add(task);
            }
            return result;
        });

        // 3. Update UI-bound tasks and add the new tasks on the UI thread.
        void ApplyQueueChanges()
        {
            var profile = FormatRegistry.GetProfile(targetFormat);
            foreach (var existingTask in existingTasksToReset.Distinct())
            {
                if (Tasks.Contains(existingTask))
                {
                    ResetTask(existingTask, targetFormat, outputDir, bitrate, sampleRate, channels, profile.Extension);
                }
            }

            var queuedSources = new HashSet<string>(
                Tasks.Select(existingTask => Path.GetFullPath(existingTask.SourcePath)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var task in tasksCreated)
            {
                if (!queuedSources.Add(Path.GetFullPath(task.SourcePath)))
                {
                    continue;
                }

                task.TargetPath = ResolveSafeTargetPath(
                    task.SourcePath,
                    outputDir,
                    profile.Extension,
                    Tasks.Select(existingTask => existingTask.TargetPath));
                Tasks.Add(task);
                addedCount++;
            }
        }

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.Invoke(ApplyQueueChanges);
        }
        else
        {
            ApplyQueueChanges();
        }

        if (addedCount > 0)
        {
            LogMessage?.Invoke($"Added {addedCount} files to queue.");
        }
        else if (existingTasksToReset.Count > 0)
        {
            LogMessage?.Invoke($"Reset {existingTasksToReset.Distinct().Count()} files for conversion.");
        }
        EmitProgress();
        return addedCount;
    }

    public void RemoveTask(ConversionTask task)
    {
        if (IsConverting)
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
                ResetTask(task, targetFormat, outputDir, bitrate, sampleRate, channels, profile.Extension);
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
                var profile = FormatRegistry.GetProfile(t.TargetFormat);
                ResetTask(
                    t,
                    t.TargetFormat,
                    Path.GetDirectoryName(t.TargetPath),
                    t.Bitrate,
                    t.SampleRate,
                    t.Channels,
                    profile.Extension);
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
        var conversionCts = new CancellationTokenSource();
        _conversionCts = conversionCts;
        var token = conversionCts.Token;

        var engine = new ConversionEngine(_locator.FFmpegPath);
        var maxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 8);
        var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

        LogMessage?.Invoke($"Starting batch conversion of {pendingTasks.Count} tasks ({maxDegreeOfParallelism} concurrent workers)...");

        int completed = Tasks.Count(t => t.Status == Models.TaskStatus.Completed);
        int total = Tasks.Count;

        try
        {
            var runningTasks = pendingTasks.Select(async task =>
            {
                var semaphoreAcquired = false;
                try
                {
                    await semaphore.WaitAsync(token);
                    semaphoreAcquired = true;

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
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    if (task.Status == Models.TaskStatus.Pending)
                    {
                        task.Status = Models.TaskStatus.Cancelled;
                    }
                }
                finally
                {
                    if (semaphoreAcquired)
                    {
                        semaphore.Release();
                    }
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
            semaphore.Dispose();
            conversionCts.Dispose();
            _conversionCts = null;
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

    private void ResetTask(
        ConversionTask task,
        string targetFormat,
        string? outputDir,
        string bitrate,
        string sampleRate,
        string channels,
        string targetExtension)
    {
        task.TargetFormat = targetFormat;
        task.Bitrate = bitrate;
        task.SampleRate = sampleRate;
        task.Channels = channels;
        task.TargetPath = ResolveSafeTargetPath(
            task.SourcePath,
            outputDir,
            targetExtension,
            Tasks.Where(otherTask => otherTask != task).Select(otherTask => otherTask.TargetPath));
        task.Status = Models.TaskStatus.Pending;
        task.Progress = 0.0;
        task.ErrorMessage = null;
        task.ActualFinalFileSize = null;
    }
}
