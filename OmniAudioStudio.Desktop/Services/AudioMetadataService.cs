using System.IO;

namespace OmniAudioStudio.Desktop.Services;

public class AudioMetadataService
{
    public (TimeSpan Duration, int Bitrate, int SampleRate, int Channels) ProbeAudio(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file?.Properties != null)
            {
                return (
                    file.Properties.Duration,
                    file.Properties.AudioBitrate,
                    file.Properties.AudioSampleRate,
                    file.Properties.AudioChannels
                );
            }
        }
        catch
        {
            // TagLib fallback - if file header can't be parsed, return empty defaults
        }

        return (TimeSpan.Zero, 0, 0, 0);
    }
}
