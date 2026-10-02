using System.Diagnostics;
using System.IO;
using OmniAudioStudio.Desktop.Models;
using OmniAudioStudio.Desktop.Services;

Console.WriteLine("ControlAppearance values: " + string.Join(", ", Enum.GetNames(typeof(Wpf.Ui.Controls.ControlAppearance))));

// 1. Validator & FFmpeg Discovery
Console.WriteLine("\n[1] Testing FFmpeg Discovery...");
var locator = new FFmpegLocator();
if (!locator.IsReady || string.IsNullOrEmpty(locator.FFmpegPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] FFmpeg not found! Path: {locator.FFmpegPath}");
    Console.ResetColor();
    return 1;
}
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[PASS] FFmpeg discovered at: {locator.FFmpegPath}");
Console.WriteLine($"       Banner: {locator.VersionBanner}");
Console.ResetColor();

// Setup test directory
var scratchDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_scratch");
Directory.CreateDirectory(scratchDir);
var srcWav = Path.Combine(scratchDir, "test_sample.wav");

// Generate 1.5s sine tone via FFmpeg
var genPsi = new ProcessStartInfo
{
    FileName = locator.FFmpegPath,
    Arguments = $"-y -f lavfi -i sine=frequency=1000:sample_rate=44100:duration=1.5 -ac 2 \"{srcWav}\"",
    UseShellExecute = false,
    CreateNoWindow = true
};
using (var p = Process.Start(genPsi))
{
    p?.WaitForExit();
}

// 2. Audio Metadata & Duration
Console.WriteLine("\n[2] Testing Fast Metadata & Duration Probing (TagLib)...");
var metaService = new AudioMetadataService();
var sw = Stopwatch.StartNew();
var (duration, bitrate, sampleRate, channels) = metaService.ProbeAudio(srcWav);
sw.Stop();
Console.WriteLine($"Probing took: {sw.Elapsed.TotalMilliseconds:F2} ms (Target was < 10 ms)");

if (Math.Abs(duration.TotalSeconds - 1.5) > 0.3)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Duration expected ~1.5s, got: {duration.TotalSeconds}s");
    Console.ResetColor();
    return 1;
}
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[PASS] Accurate Duration probed: {duration.TotalSeconds:F2}s");
Console.ResetColor();

// 3. Safe Target Path Resolution
Console.WriteLine("\n[3] Testing Safe Target Path Overwrite Protection...");
var safePath = QueueManager.ResolveSafeTargetPath(srcWav, scratchDir, ".wav");
if (Path.GetFileName(safePath) != "test_sample_converted.wav")
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Expected 'test_sample_converted.wav', got: {Path.GetFileName(safePath)}");
    Console.ResetColor();
    return 1;
}
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[PASS] Self-overwrite prevented: {Path.GetFileName(safePath)}");
Console.ResetColor();

// 4. Queue Duplicate Blocking
Console.WriteLine("\n[4] Testing Queue Duplicate Blocking & Parallelism...");
var qm = new QueueManager(locator, metaService);
int addedFirst = await qm.AddPathsAsync(new[] { srcWav }, "MP3", scratchDir, "320 kbps", "Original", "Original");
int addedDuplicate = await qm.AddPathsAsync(new[] { srcWav }, "MP3", scratchDir, "320 kbps", "Original", "Original");

if (addedFirst != 1 || addedDuplicate != 0 || qm.Tasks.Count != 1)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Duplicate detection failed! First: {addedFirst}, Duplicate: {addedDuplicate}");
    Console.ResetColor();
    return 1;
}
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[PASS] Duplicates successfully blocked from queue.");
Console.ResetColor();

// 5. Test Conversion across Formats (MP3, FLAC, AAC, OPUS, OGG)
Console.WriteLine("\n[5] Testing Conversion Engine across Formats...");
var engine = new ConversionEngine(locator.FFmpegPath);
var testFormats = new[] { "MP3", "FLAC", "AAC", "OPUS", "OGG" };

foreach (var fmt in testFormats)
{
    var profile = FormatRegistry.GetProfile(fmt);
    var targetFile = Path.Combine(scratchDir, $"converted_test{profile.Extension}");
    var task = new ConversionTask
    {
        SourcePath = srcWav,
        FileName = Path.GetFileName(srcWav),
        FileSize = new FileInfo(srcWav).Length,
        Duration = duration,
        SourceFormat = "WAV",
        TargetFormat = fmt,
        TargetPath = targetFile,
        Bitrate = "320 kbps",
        SampleRate = "Original",
        Channels = "Original"
    };

    var convSw = Stopwatch.StartNew();
    bool success = await engine.ConvertAsync(task, null, CancellationToken.None);
    convSw.Stop();

    if (!success || !File.Exists(targetFile) || new FileInfo(targetFile).Length == 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] Conversion to {fmt} failed: {task.ErrorMessage}");
        Console.ResetColor();
        return 1;
    }
    Console.WriteLine($"[PASS] {fmt.PadRight(5)} -> Converted in {convSw.ElapsedMilliseconds} ms (Size: {new FileInfo(targetFile).Length} bytes)");
}

// 6. Test FLAC Source with FLAC Target Format (Target format stability)
Console.WriteLine("\n[6] Testing FLAC source with FLAC target format stability & re-conversion...");
var flacSrc = Path.Combine(scratchDir, "sample_audio.flac");
var genFlacPsi = new ProcessStartInfo
{
    FileName = locator.FFmpegPath,
    Arguments = $"-y -f lavfi -i sine=frequency=440:duration=1.0 \"{flacSrc}\"",
    UseShellExecute = false,
    CreateNoWindow = true
};
using (var p = Process.Start(genFlacPsi)) { p?.WaitForExit(); }

var qmFlac = new QueueManager(locator, metaService);
int addedFlac = await qmFlac.AddPathsAsync(new[] { flacSrc }, "FLAC", scratchDir, "Original", "Original", "Original");
if (addedFlac != 1 || qmFlac.Tasks.Count != 1)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Adding FLAC file failed. Count: {qmFlac.Tasks.Count}");
    Console.ResetColor();
    return 1;
}

var flacTask = qmFlac.Tasks[0];
if (flacTask.TargetFormat != "FLAC")
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] TargetFormat was knocked off! Expected 'FLAC', got: '{flacTask.TargetFormat}'");
    Console.ResetColor();
    return 1;
}

if (!flacTask.TargetPath.EndsWith("sample_audio_converted.flac"))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Safe target path expected 'sample_audio_converted.flac', got: '{flacTask.TargetPath}'");
    Console.ResetColor();
    return 1;
}

var estBeforeFlac = flacTask.EstimatedOrActualTargetSize;
if (!estBeforeFlac.StartsWith("~"))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] EstimatedOrActualTargetSize should be estimated (~ prefix) before conversion, got: {estBeforeFlac}");
    Console.ResetColor();
    return 1;
}

await qmFlac.StartConversionAsync();
if (flacTask.Status != OmniAudioStudio.Desktop.Models.TaskStatus.Completed || !File.Exists(flacTask.TargetPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Conversion to FLAC failed! Status: {flacTask.Status}, Error: {flacTask.ErrorMessage}");
    Console.ResetColor();
    return 1;
}

var actualSizeFlac = flacTask.EstimatedOrActualTargetSize;
if (actualSizeFlac.StartsWith("~"))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] EstimatedOrActualTargetSize should be exact (no ~ prefix) after conversion, got: {actualSizeFlac}");
    Console.ResetColor();
    return 1;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[PASS] FLAC -> FLAC converted successfully: {Path.GetFileName(flacTask.TargetPath)} ({actualSizeFlac}) [Estimate was {estBeforeFlac}]");

// Test updating completed task format and re-converting
qmFlac.UpdatePendingTasksFormat("MP3", scratchDir, "320 kbps", "Original", "Original");
if (flacTask.Status != OmniAudioStudio.Desktop.Models.TaskStatus.Pending || flacTask.TargetFormat != "MP3")
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Updating completed task to MP3 failed! Status: {flacTask.Status}, Target: {flacTask.TargetFormat}");
    Console.ResetColor();
    return 1;
}

var estBeforeMp3 = flacTask.EstimatedOrActualTargetSize;
if (!estBeforeMp3.StartsWith("~"))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Estimated size for reverted MP3 should start with ~, got: {estBeforeMp3}");
    Console.ResetColor();
    return 1;
}

await qmFlac.StartConversionAsync();
if (flacTask.Status != OmniAudioStudio.Desktop.Models.TaskStatus.Completed || !flacTask.TargetPath.EndsWith(".mp3") || !File.Exists(flacTask.TargetPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Re-converting to MP3 failed! Status: {flacTask.Status}, Error: {flacTask.ErrorMessage}");
    Console.ResetColor();
    return 1;
}

var actualSizeMp3 = flacTask.EstimatedOrActualTargetSize;
if (actualSizeMp3.StartsWith("~"))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] Final MP3 size should be exact (no ~ prefix), got: {actualSizeMp3}");
    Console.ResetColor();
    return 1;
}

Console.WriteLine($"[PASS] Re-conversion to MP3 succeeded: {Path.GetFileName(flacTask.TargetPath)} ({actualSizeMp3}) [Estimate was {estBeforeMp3}]");
Console.ResetColor();

// Cleanup scratch
try { Directory.Delete(scratchDir, true); } catch { }

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("\n====================================================");
Console.WriteLine("  ALL 6 TEST SUITES PASSED FLAWLESSLY ON .NET 8!    ");
Console.WriteLine("====================================================");
Console.ResetColor();

return 0;
