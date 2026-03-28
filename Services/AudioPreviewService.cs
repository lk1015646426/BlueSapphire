using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public enum AudioPreviewLoopMode
    {
        Off,
        All,
        One
    }

    public sealed record AudioPreviewState(
        string? CurrentPath,
        bool HasSource,
        bool IsPlaying,
        TimeSpan Position,
        TimeSpan Duration);

    public class AudioPreviewService : IDisposable
    {
        private readonly MediaPlayer _player;
        private string? _currentPath;
        private bool _disposed;

        public event EventHandler<AudioPreviewState>? StateChanged;
        public event EventHandler? PlaybackEnded;

        public AudioPreviewService()
        {
            _player = new MediaPlayer
            {
                AutoPlay = false
            };

            _player.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            _player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
            _player.PlaybackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;
            _player.MediaEnded += Player_MediaEnded;
        }

        public async Task<bool> LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            if (string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                RaiseStateChanged();
                return true;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            _player.Pause();
            _player.Source = MediaSource.CreateFromStorageFile(file);
            _player.PlaybackSession.Position = TimeSpan.Zero;
            _currentPath = path;
            RaiseStateChanged();
            return true;
        }

        public void TogglePlayPause()
        {
            if (_player.Source == null)
            {
                return;
            }

            if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                _player.Pause();
            }
            else
            {
                _player.Play();
            }

            RaiseStateChanged();
        }

        public void Play()
        {
            if (_player.Source == null)
            {
                return;
            }

            _player.Play();
            RaiseStateChanged();
        }

        public void Pause()
        {
            _player.Pause();
            RaiseStateChanged();
        }

        public void Stop()
        {
            _player.Pause();
            _player.Source = null;
            _currentPath = null;
            RaiseStateChanged();
        }

        public void Seek(TimeSpan position)
        {
            TimeSpan duration = _player.PlaybackSession.NaturalDuration;
            _player.PlaybackSession.Position = Clamp(position, duration);
            RaiseStateChanged();
        }

        public void Skip(TimeSpan delta)
        {
            Seek(_player.PlaybackSession.Position + delta);
        }

        public AudioPreviewState GetState()
        {
            return new AudioPreviewState(
                _currentPath,
                _player.Source != null,
                _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing,
                _player.PlaybackSession.Position,
                _player.PlaybackSession.NaturalDuration);
        }

        public static string FormatTimestamp(TimeSpan value)
        {
            if (value <= TimeSpan.Zero)
            {
                return "00:00";
            }

            return value.TotalHours >= 1
                ? value.ToString(@"hh\:mm\:ss")
                : value.ToString(@"mm\:ss");
        }

        public static TimeSpan Clamp(TimeSpan position, TimeSpan duration)
        {
            if (position < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (duration > TimeSpan.Zero && position > duration)
            {
                return duration;
            }

            return position;
        }

        public static int ResolveAdjacentIndex(int currentIndex, int count, int offset, bool allowWrap)
        {
            if (count <= 0 || currentIndex < 0 || currentIndex >= count || offset == 0)
            {
                return -1;
            }

            int targetIndex = currentIndex + offset;
            if (targetIndex >= 0 && targetIndex < count)
            {
                return targetIndex;
            }

            if (!allowWrap)
            {
                return -1;
            }

            return offset > 0 ? 0 : count - 1;
        }

        private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            RaiseStateChanged();
        }

        private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            RaiseStateChanged();
        }

        private void PlaybackSession_NaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
            RaiseStateChanged();
        }

        private void Player_MediaEnded(MediaPlayer sender, object args)
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            RaiseStateChanged();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseStateChanged()
        {
            if (_disposed)
            {
                return;
            }

            StateChanged?.Invoke(this, GetState());
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _player.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
            _player.PlaybackSession.PositionChanged -= PlaybackSession_PositionChanged;
            _player.PlaybackSession.NaturalDurationChanged -= PlaybackSession_NaturalDurationChanged;
            _player.MediaEnded -= Player_MediaEnded;
            _player.Dispose();
        }
    }
}
