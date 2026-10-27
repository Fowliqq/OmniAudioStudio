using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OmniAudioStudio.Desktop.Models;

namespace OmniAudioStudio.Desktop.Services;

public class ConversionEngine
{
    private readonly string _ffmpegPath;

    public ConversionEngine(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<bool> ConvertAsync(
        ConversionTask task,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
        {
            task.Status = Models.TaskStatus.Failed;
            task.ErrorMessage = "FFmpeg executable was not found.";
            return false;
        }

        if (!File.Exists(task.SourcePath))
        {
            task.Status = Models.TaskStatus.Failed;
            task.ErrorMessage = $"Source file not found: {Path.GetFileName(task.SourcePath)}";
            return false;
        }

        string? tempOutputPath = null;
        try
        {
            var targetPath = Path.GetFullPath(task.TargetPath);
            var targetDir = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(targetDir);

            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                task.Status = Models.TaskStatus.Failed;
                task.ErrorMessage = $"Output file already exists and was left untouched: {Path.GetFileName(targetPath)}";
                return false;
            }

            task.TargetPath = targetPath;
            tempOutputPath = Path.Combine(
                targetDir,
                $"{Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}.partial{Path.GetExtension(targetPath)}");

            var profile = FormatRegistry.GetProfile(task.TargetFormat);
            var formatArgs = profile.BuildFFmpegArgs(task.Bitrate, task.SampleRate, task.Channels);

            var argsList = new List<string>
            {
                "-n",
                "-i", task.SourcePath,
                "-map_metadata", "0",
                "-vn",
                "-loglevel", "error"
            };
            argsList.AddRange(formatArgs);
            argsList.AddRange(new[]
            {
                "-progress", "pipe:1",
                "-nostats",
                tempOutputPath
            });

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in argsList)
            {
                psi.ArgumentList.Add(arg);
            }

            task.Status = Models.TaskStatus.Converting;
            task.Progress = 0.0;
            task.ErrorMessage = null;

            using var process = new Process { StartInfo = psi };
            var stderrBuilder = new StringBuilder();
            var durationMicroseconds = task.Duration.TotalMilliseconds * 1000.0;
            var timeRegex = new Regex(@"^out_time_us=(\d+)", RegexOptions.Compiled);

            process.Start();

            // Background task to read stderr
            var stderrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        stderrBuilder.AppendLine(line);
                    }
                }
            });

            // Reading stdout for progress updates
            var stdoutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    var match = timeRegex.Match(line.Trim());
                    if (match.Success && durationMicroseconds > 0)
                    {
                        if (long.TryParse(match.Groups[1].Value, out var currentUs))
                        {
                            var pct = Math.Clamp((currentUs / durationMicroseconds) * 100.0, 0.0, 100.0);
                            task.Progress = Math.Round(pct, 1);
                            progress?.Report(task.Progress);
                        }
                    }
                }
            });

            // Monitor cancellation
            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
            });

            await Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), stderrTask, stdoutTask);

            if (cancellationToken.IsCancellationRequested)
            {
                task.Status = Models.TaskStatus.Cancelled;
                task.Progress = 0.0;
                return false;
            }

            if (process.ExitCode == 0)
            {
                if (!File.Exists(tempOutputPath) || new FileInfo(tempOutputPath).Length == 0)
                {
                    task.Status = Models.TaskStatus.Failed;
                    task.ErrorMessage = "FFmpeg exited successfully but did not produce a valid output file.";
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Move without overwrite so an output created during conversion is preserved.
                var outputSize = new FileInfo(tempOutputPath).Length;
                File.Move(tempOutputPath, targetPath);
                task.Status = Models.TaskStatus.Completed;
                task.Progress = 100.0;
                task.ActualFinalFileSize = outputSize;
                return true;
            }
            else
            {
                task.Status = Models.TaskStatus.Failed;
                var err = stderrBuilder.ToString().Trim();
                task.ErrorMessage = string.IsNullOrEmpty(err) ? $"FFmpeg error (code {process.ExitCode})" : err;
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            task.Status = Models.TaskStatus.Cancelled;
            task.Progress = 0.0;
            return false;
        }
        catch (Exception ex)
        {
            task.Status = Models.TaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempOutputPath))
            {
                try
                {
                    if (File.Exists(tempOutputPath))
                    {
                        File.Delete(tempOutputPath);
                    }
                }
                catch { }
            }
        }
    }
}
