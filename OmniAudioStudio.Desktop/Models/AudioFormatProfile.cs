using System.Text.RegularExpressions;

namespace OmniAudioStudio.Desktop.Models;

public record AudioFormatProfile(
    string Name,
    string Extension,
    string Codec,
    string Description,
    bool IsLossless,
    string? DefaultBitrate = null)
{
    public IReadOnlyList<string> BuildFFmpegArgs(string bitrateStr, string sampleRateStr, string channelsStr)
    {
        var args = new List<string> { "-codec:a", Codec };

        // Bitrate
        if (!IsLossless)
        {
            var match = Regex.Match(bitrateStr, @"(\d+)\s*kbps", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                int kbps = int.Parse(match.Groups[1].Value);
                if (Name.Equals("OPUS", StringComparison.OrdinalIgnoreCase) && kbps > 256)
                {
                    kbps = 256;
                }
                else if (Name.Equals("OGG", StringComparison.OrdinalIgnoreCase) && kbps > 192 && channelsStr.Contains("Mono", StringComparison.OrdinalIgnoreCase))
                {
                    kbps = 192;
                }
                args.AddRange(new[] { "-b:a", $"{kbps}k" });
            }
            else if (!string.IsNullOrEmpty(DefaultBitrate))
            {
                args.AddRange(new[] { "-b:a", DefaultBitrate });
            }
        }

        // Sample Rate
        if (Name.Equals("OPUS", StringComparison.OrdinalIgnoreCase))
        {
            var supportedOpusRates = new HashSet<string> { "8000", "12000", "16000", "24000", "48000" };
            string chosenRate = "48000";
            if (!string.IsNullOrEmpty(sampleRateStr) && !sampleRateStr.Contains("Original", StringComparison.OrdinalIgnoreCase))
            {
                var rateMatch = Regex.Match(sampleRateStr, @"(\d+)\s*Hz", RegexOptions.IgnoreCase);
                if (rateMatch.Success && supportedOpusRates.Contains(rateMatch.Groups[1].Value))
                {
                    chosenRate = rateMatch.Groups[1].Value;
                }
            }
            args.AddRange(new[] { "-ar", chosenRate });
        }
        else if (!string.IsNullOrEmpty(sampleRateStr) && !sampleRateStr.Contains("Original", StringComparison.OrdinalIgnoreCase))
        {
            var rateMatch = Regex.Match(sampleRateStr, @"(\d+)\s*Hz", RegexOptions.IgnoreCase);
            if (rateMatch.Success)
            {
                args.AddRange(new[] { "-ar", rateMatch.Groups[1].Value });
            }
        }

        // Channels
        if (!string.IsNullOrEmpty(channelsStr) && !channelsStr.Contains("Original", StringComparison.OrdinalIgnoreCase))
        {
            if (channelsStr.Contains("Stereo", StringComparison.OrdinalIgnoreCase))
            {
                args.AddRange(new[] { "-ac", "2" });
            }
            else if (channelsStr.Contains("Mono", StringComparison.OrdinalIgnoreCase))
            {
                args.AddRange(new[] { "-ac", "1" });
            }
        }

        // Specific flags
        switch (Name.ToUpperInvariant())
        {
            case "MP3":
                args.AddRange(new[] { "-id3v2_version", "3", "-write_id3v1", "1" });
                break;
            case "FLAC":
                args.AddRange(new[] { "-compression_level", "5" });
                break;
            case "OPUS":
                args.AddRange(new[] { "-vbr", "on" });
                break;
            case "AAC":
            case "ALAC":
                args.AddRange(new[] { "-movflags", "+faststart" });
                break;
        }

        return args;
    }
}

public static class FormatRegistry
{
    private static readonly Dictionary<string, AudioFormatProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MP3"] = new("MP3", ".mp3", "libmp3lame", "MPEG Audio Layer III (Universal Compatibility)", false, "320k"),
        ["FLAC"] = new("FLAC", ".flac", "flac", "Free Lossless Audio Codec (Bit-perfect compression)", true),
        ["WAV"] = new("WAV", ".wav", "pcm_s16le", "Waveform Audio 16-bit PCM (Uncompressed Studio Master)", true),
        ["AAC"] = new("AAC", ".m4a", "aac", "Advanced Audio Coding (High efficiency, Apple standard)", false, "256k"),
        ["OGG"] = new("OGG", ".ogg", "libvorbis", "Ogg Vorbis (Open source high-fidelity lossy codec)", false, "192k"),
        ["OPUS"] = new("OPUS", ".opus", "libopus", "IETF Opus (State-of-the-art interactive & streaming codec)", false, "128k"),
        ["ALAC"] = new("ALAC", ".m4a", "alac", "Apple Lossless Audio Codec (iTunes / iOS Lossless)", true),
        ["AIFF"] = new("AIFF", ".aiff", "pcm_s16be", "Audio Interchange File Format (Apple uncompressed)", true),
        ["WMA"] = new("WMA", ".wma", "wmav2", "Windows Media Audio 2", false, "192k")
    };

    public static IReadOnlyList<string> AvailableFormats => Profiles.Keys.ToList();

    public static AudioFormatProfile GetProfile(string formatName)
    {
        if (Profiles.TryGetValue(formatName, out var profile))
            return profile;
        return Profiles["MP3"];
    }

    public static readonly HashSet<string> SupportedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma",
        ".aiff", ".aif", ".ape", ".alac", ".wv", ".mka"
    };

    public static string DetectSourceFormatName(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => "MP3",
            ".flac" or ".ape" or ".wv" => "FLAC",
            ".wav" => "WAV",
            ".m4a" or ".aac" => "AAC",
            ".ogg" => "OGG",
            ".opus" or ".mka" => "OPUS",
            ".wma" => "WMA",
            ".aiff" or ".aif" => "AIFF",
            ".alac" => "ALAC",
            _ => extension.TrimStart('.').ToUpperInvariant()
        };
    }
}
