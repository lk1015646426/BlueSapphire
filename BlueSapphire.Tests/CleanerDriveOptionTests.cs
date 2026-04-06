using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class CleanerDriveOptionTests
{
    [Fact]
    public void DriveOption_ComputesUsageValues()
    {
        CleanerDriveOption option = new()
        {
            Name = "D:",
            VolumeLabel = "Media",
            TotalBytes = 200,
            FreeBytes = 50,
            FileSystem = "NTFS",
            DriveKindText = "本地磁盘"
        };

        Assert.Equal(150, option.UsedBytes);
        Assert.Equal(75, option.UsedPercentage);
        Assert.Contains("75", option.UsageText);
        Assert.Contains("Media", option.TitleText);
    }
}
