using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TwitchChatOverlay.Models;

namespace TwitchChatOverlay.Services
{
    public sealed class AudioOutputDeviceOption
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
    }

    public class NotificationSoundService
    {
        private const string DefaultOutputDeviceId = "";
        private const string EmbeddedSoundResourceName = "TwitchChatOverlay.NotificationSound.NotificationSound.mp3";

        private readonly SettingsService _settingsService;
        private readonly object _sync = new();
        private readonly object _embeddedSoundSync = new();
        private readonly List<PlaybackHandle> _activePlaybacks = [];
        private string _embeddedSoundFilePath;

        public NotificationSoundService(SettingsService settingsService)
        {
            this._settingsService = settingsService;
        }

        public IReadOnlyList<AudioOutputDeviceOption> GetOutputDevices()
        {
            var devices = new List<AudioOutputDeviceOption>
            {
                new() { Id = DefaultOutputDeviceId, DisplayName = "既定の出力デバイス" }
            };

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    devices.Add(new AudioOutputDeviceOption
                    {
                        Id = device.ID,
                        DisplayName = device.FriendlyName,
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.Error("出力音声デバイス一覧の取得に失敗しました", ex);
            }

            return devices;
        }

        public void PlayNotificationSound(OverlayNotification notification)
        {
            if (notification?.Type != NotificationType.Chat)
            {
                return;
            }

            this.PlaySoundCore(requireEnabledSetting: true);
        }

        public void PlayPreviewSound()
        {
            this.PlaySoundCore(this._settingsService.LoadSettings(), requireEnabledSetting: false);
        }

        public void PlayPreviewSound(AppSettings previewSettings)
        {
            this.PlaySoundCore(previewSettings ?? this._settingsService.LoadSettings(), requireEnabledSetting: false);
        }

        private void PlaySoundCore(bool requireEnabledSetting)
        {
            this.PlaySoundCore(this._settingsService.LoadSettings(), requireEnabledSetting);
        }

        private void PlaySoundCore(AppSettings settings, bool requireEnabledSetting)
        {
            NotificationSoundSource soundSource = null;
            try
            {
                settings ??= this._settingsService.LoadSettings();
                if (requireEnabledSetting && !settings.NotificationSoundEnabled)
                {
                    return;
                }

                var volumePercent = Math.Clamp(settings.NotificationSoundVolumePercent, 0, 100);
                if (volumePercent <= 0)
                {
                    return;
                }

                soundSource = this.ResolveSoundSource(settings);
                var playback = CreatePlayback(soundSource, volumePercent / 100f, settings.NotificationSoundOutputDeviceId ?? "");
                playback.Output.PlaybackStopped += (_, __) => this.ReleasePlayback(playback);

                lock (this._sync)
                {
                    this._activePlaybacks.Add(playback);
                }

                playback.Output.Play();
                soundSource = null;
            }
            catch (Exception ex)
            {
                soundSource?.Dispose();
                LogService.Error("通知音の再生に失敗しました", ex);
            }
        }

        public bool IsSupportedSoundFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var ext = Path.GetExtension(filePath);
            return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
        }

        private NotificationSoundSource ResolveSoundSource(AppSettings settings)
        {
            var customPath = settings.NotificationSoundFilePath ?? "";
            if (settings.NotificationSoundSourceMode == NotificationSoundSourceMode.CustomFile)
            {
                if (File.Exists(customPath) && this.IsSupportedSoundFile(customPath))
                {
                    return NotificationSoundSource.ForFile(customPath);
                }

                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    LogService.Error($"通知音ファイルが無効なため埋め込み音源へフォールバックします: {customPath}");
                }
            }

            return NotificationSoundSource.ForFile(this.EnsureEmbeddedSoundFilePath());
        }

        private string EnsureEmbeddedSoundFilePath()
        {
            lock (this._embeddedSoundSync)
            {
                if (!string.IsNullOrWhiteSpace(this._embeddedSoundFilePath) && File.Exists(this._embeddedSoundFilePath))
                {
                    return this._embeddedSoundFilePath;
                }

                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TwitchChatOverlay",
                    "cache");
                _ = Directory.CreateDirectory(baseDir);

                var targetPath = Path.Combine(baseDir, "embedded-notification-sound.mp3");

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedSoundResourceName);
                if (stream == null)
                {
                    throw new FileNotFoundException($"埋め込み通知音リソースが見つかりません: {EmbeddedSoundResourceName}");
                }

                var shouldWrite = !File.Exists(targetPath);
                if (!shouldWrite)
                {
                    var fileLength = new FileInfo(targetPath).Length;
                    shouldWrite = fileLength != stream.Length;
                }

                if (shouldWrite)
                {
                    stream.Position = 0;
                    using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    stream.CopyTo(fileStream);
                }

                this._embeddedSoundFilePath = targetPath;
                return targetPath;
            }
        }

        private static PlaybackHandle CreatePlayback(NotificationSoundSource soundSource, float volume, string outputDeviceId)
        {
            try
            {
                var (sampleProvider, disposableSource) = CreateSampleProvider(soundSource);
                var volumeProvider = sampleProvider;
                if (volume < 0.999f)
                {
                    volumeProvider = new VolumeSampleProvider(sampleProvider) { Volume = volume };
                }

                var output = CreateOutputDevice(outputDeviceId);
                output.Init(volumeProvider.ToWaveProvider());
                return new PlaybackHandle(output, disposableSource, soundSource);
            }
            catch
            {
                soundSource.Dispose();
                throw;
            }
        }

        private static (ISampleProvider sampleProvider, IDisposable disposableSource) CreateSampleProvider(NotificationSoundSource soundSource)
        {
            if (soundSource.FilePath != null)
            {
                var ext = Path.GetExtension(soundSource.FilePath);
                if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
                {
                    var vorbisReader = new VorbisWaveReader(soundSource.FilePath);
                    return (vorbisReader.ToSampleProvider(), vorbisReader);
                }

                var audioFileReader = new AudioFileReader(soundSource.FilePath);
                return (audioFileReader, audioFileReader);
            }

            if (soundSource.BackingStream == null)
            {
                throw new InvalidOperationException("通知音ソースが初期化されていません。");
            }

            var mp3Reader = new Mp3FileReader(soundSource.BackingStream);
            return (mp3Reader.ToSampleProvider(), mp3Reader);
        }

        private static IWavePlayer CreateOutputDevice(string outputDeviceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputDeviceId))
                {
                    return new WasapiOut(AudioClientShareMode.Shared, true, 120);
                }

                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(outputDeviceId);
                return new WasapiOut(device, AudioClientShareMode.Shared, true, 120);
            }
            catch (Exception ex)
            {
                LogService.Error($"出力デバイスの初期化に失敗したため既定デバイスへフォールバックします: {outputDeviceId}", ex);
                return new WasapiOut(AudioClientShareMode.Shared, true, 120);
            }
        }

        private void ReleasePlayback(PlaybackHandle playback)
        {
            lock (this._sync)
            {
                _ = this._activePlaybacks.Remove(playback);
            }

            playback.Dispose();
        }

        private sealed class NotificationSoundSource : IDisposable
        {
            public string FilePath { get; }
            public Stream BackingStream { get; }

            private NotificationSoundSource(string filePath, Stream backingStream)
            {
                this.FilePath = filePath;
                this.BackingStream = backingStream;
            }

            public static NotificationSoundSource ForFile(string filePath)
            {
                return new(filePath, null);
            }

            public static NotificationSoundSource ForEmbedded(Stream stream)
            {
                return new(null, stream);
            }

            public void Dispose()
            {
                this.BackingStream?.Dispose();
            }
        }

        private sealed class PlaybackHandle : IDisposable
        {
            public IWavePlayer Output { get; }

            private readonly IDisposable _disposableSource;
            private readonly NotificationSoundSource _soundSource;

            public PlaybackHandle(IWavePlayer output, IDisposable disposableSource, NotificationSoundSource soundSource)
            {
                this.Output = output;
                this._disposableSource = disposableSource;
                this._soundSource = soundSource;
            }

            public void Dispose()
            {
                this.Output.Dispose();
                this._disposableSource?.Dispose();
                this._soundSource?.Dispose();
            }
        }
    }
}