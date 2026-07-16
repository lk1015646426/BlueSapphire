using BlueSapphire.Models;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerProfileService
    {
        private static readonly string[] AllowedChannels = ["stable", "canary", "internal"];

        private readonly CleanerStateStore _stateStore;

        public CleanerProfileService(CleanerStateStore stateStore)
        {
            _stateStore = stateStore;
        }

        public async Task<CleanerProfileState> GetProfileAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.UpdatePreferencesAsync(
                static state => NormalizePreferences(state));
            return BuildProfile(preferences);
        }

        public async Task<CleanerProfileState> SetRolloutChannelAsync(string rolloutChannel)
        {
            CleanerPreferenceState preferences = await _stateStore.UpdatePreferencesAsync(state =>
            {
                state.DeviceProfileId = EnsureDeviceProfileId(state.DeviceProfileId);
                state.RolloutChannel = NormalizeChannel(rolloutChannel);
            });
            return BuildProfile(preferences);
        }

        public static string NormalizeChannel(string? rolloutChannel)
        {
            string normalized = (rolloutChannel ?? string.Empty).Trim().ToLowerInvariant();
            return AllowedChannels.Contains(normalized, StringComparer.OrdinalIgnoreCase)
                ? normalized
                : "stable";
        }

        public static int ComputeBucket(string deviceProfileId)
        {
            string normalized = EnsureDeviceProfileId(deviceProfileId);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            int value = BitConverter.ToInt32(hash, 0) & int.MaxValue;
            return value % 100;
        }

        private static bool NormalizePreferences(CleanerPreferenceState preferences)
        {
            string profileId = EnsureDeviceProfileId(preferences.DeviceProfileId);
            string rolloutChannel = NormalizeChannel(preferences.RolloutChannel);

            if (string.Equals(profileId, preferences.DeviceProfileId, StringComparison.Ordinal) &&
                string.Equals(rolloutChannel, preferences.RolloutChannel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            preferences.DeviceProfileId = profileId;
            preferences.RolloutChannel = rolloutChannel;
            return true;
        }

        private static CleanerProfileState BuildProfile(CleanerPreferenceState preferences)
        {
            string profileId = EnsureDeviceProfileId(preferences.DeviceProfileId);
            string rolloutChannel = NormalizeChannel(preferences.RolloutChannel);
            return new CleanerProfileState
            {
                DeviceProfileId = profileId,
                RolloutChannel = rolloutChannel,
                DeviceBucket = ComputeBucket(profileId)
            };
        }

        private static string EnsureDeviceProfileId(string? currentValue)
        {
            return string.IsNullOrWhiteSpace(currentValue)
                ? Guid.NewGuid().ToString("N")
                : currentValue.Trim();
        }
    }
}
