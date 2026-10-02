using System.Diagnostics;
using System.IO;

namespace OmniAudioStudio.Desktop.Services;

public class FFmpegLocator
{
    public string? FFmpegPath { get; private set; }
    public string? FFprobePath { get; private set; }
    public string? VersionBanner { get; private set; }
    public bool IsReady => !string.IsNullOrEmpty(FFmpegPath) && File.Exists(FFmpegPath);

    public FFmpegLocator()
    {
        LocateBinaries();
    }

    public void LocateBinaries()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

        var candidateDirs = new[]
        {
            Path.Combine(baseDir, "bin"),
            baseDir,
            Path.Combine(baseDir, "..", "bin"),
            Path.Combine(baseDir, "..", "..", "bin"),
            Path.Combine(baseDir, "..", "..", "..", "bin"),
            Path.Combine(projectRoot, "bin"),
            projectRoot,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ffmpeg", "bin")
        };

        foreach (var dir in candidateDirs)
        {
            var ffmpegCandidate = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(ffmpegCandidate) && string.IsNullOrEmpty(FFmpegPath))
            {
                FFmpegPath = ffmpegCandidate;
            }

            var ffprobeCandidate = Path.Combine(dir, "ffprobe.exe");
            if (File.Exists(ffprobeCandidate) && string.IsNullOrEmpty(FFprobePath))
            {
                FFprobePath = ffprobeCandidate;
            }
        }

        // Fallback to system PATH
        if (string.IsNullOrEmpty(FFmpegPath))
        {
            FFmpegPath = FindInPath("ffmpeg.exe");
        }
        if (string.IsNullOrEmpty(FFprobePath))
        {
            FFprobePath = FindInPath("ffprobe.exe");
        }

        if (!string.IsNullOrEmpty(FFmpegPath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var line = proc.StandardOutput.ReadLine();
                    proc.WaitForExit(2000);
                    VersionBanner = line;
                }
            }
            catch
            {
                VersionBanner = "FFmpeg found (version check timed out)";
            }
        }
    }

    private static string? FindInPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var full = Path.Combine(p, exeName);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // ignore invalid path segments
            }
        }
        return null;
    }
}
