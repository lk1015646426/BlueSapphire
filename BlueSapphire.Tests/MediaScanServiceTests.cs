using BlueSapphire.Services;
using Windows.Storage;

namespace BlueSapphire.Tests;

public class MediaScanServiceTests
{
    [Fact]
    public void HammingDistance_ReturnsBitDifferenceCount()
    {
        var leftWords = new ulong[PerceptualHash.WordCount];
        leftWords[0] = 0b101101;
        var rightWords = new ulong[PerceptualHash.WordCount];
        rightWords[0] = 0b111001;

        Assert.Equal(2, MediaScanService.HammingDistance(new PerceptualHash(leftWords), new PerceptualHash(rightWords)));

        // 跨 64 位字边界：不同字中的位也要计入
        var crossWordsA = new ulong[PerceptualHash.WordCount];
        crossWordsA[7] = 1UL << 62;
        crossWordsA[15] = 1UL << 10;
        var crossWordsB = new ulong[PerceptualHash.WordCount];

        Assert.Equal(2, MediaScanService.HammingDistance(new PerceptualHash(crossWordsA), new PerceptualHash(crossWordsB)));
    }

    [Fact]
    public async Task ComputeQuickHeaderFooterHashAsync_ReturnsHashForRealImageSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-image.png"));

        string hash = await MediaScanService.ComputeQuickHeaderFooterHashAsync(file);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public async Task ComputeSHA256Async_ReturnsHashForRealImageSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-image.png"));

        string hash = await MediaScanService.ComputeSHA256Async(file);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public async Task ComputeDHashAsync_ReturnsHashForRealImageSample()
    {
        string path = GetTestDataPath("structured-sample.png");

        PerceptualHash? hash = await MediaScanService.ComputeDHashAsync(path);

        Assert.True(hash.HasValue);
    }

    [Fact]
    public async Task ComputeDHashAsync_ReturnsNull_ForFlatSampleImage()
    {
        // sample-image.png 是 1×1 纯白图：平坦图无可用指纹，必须判为不可哈希，
        // 否则任意两张平坦图（截图、纯色底图）都会被误报为相似。
        string path = GetTestDataPath("sample-image.png");

        PerceptualHash? hash = await MediaScanService.ComputeDHashAsync(path);

        Assert.Null(hash);
    }

    [Fact]
    public void BuildDHash_ReturnsNull_ForFlatImage()
    {
        // 纯色图（白底商品图、纯色背景）：33×32 采样后无任何结构，指纹必须判为不可用，
        // 否则任意两张纯色图都会因哈希同为 0 而被报成"相似"。
        byte[] flat = CreateBgraBuffer((x, y) => 255);

        Assert.Null(MediaScanService.BuildDHash(flat));
    }

    [Fact]
    public void BuildDHash_ReturnsNull_ForLowContrastImage()
    {
        // 灰度 100→110 的弱渐变：亮度差 10×256=2560，低于 3200 的平坦阈值。
        byte[] lowContrast = CreateBgraBuffer((x, y) => (byte)(100 + (x + y) / 6));

        Assert.Null(MediaScanService.BuildDHash(lowContrast));
    }

    [Fact]
    public void BuildDHash_ReturnsNull_ForPureHorizontalGradient()
    {
        // 全幅横向渐变（0→224 严格递增）：1024 位全 0 的退化指纹，任意两张渐变图都会碰撞。
        byte[] gradient = CreateBgraBuffer((x, y) => (byte)(x * 7));

        Assert.Null(MediaScanService.BuildDHash(gradient));
    }

    [Fact]
    public void BuildDHash_ReturnsHash_ForStructuredImage()
    {
        byte[] checker = CreateBgraBuffer((x, y) => (x / 2) % 2 == 0 ? (byte)0 : (byte)255);

        PerceptualHash? hash = MediaScanService.BuildDHash(checker);

        Assert.True(hash.HasValue);
    }

    [Fact]
    public void BuildDHash_DifferentStructuredImages_AreNotReportedSimilar()
    {
        // 两种完全不同但各自结构清晰的花纹：指纹应有效，且汉明距离必须远超相似阈值，
        // 防止"明显不同的图片被列为相似"的回归。
        byte[] checker = CreateBgraBuffer((x, y) => (x / 2) % 2 == 0 ? (byte)0 : (byte)255);
        byte[] inverted = CreateBgraBuffer((x, y) => (x / 2) % 2 == 0 ? (byte)255 : (byte)0);

        PerceptualHash? hashA = MediaScanService.BuildDHash(checker);
        PerceptualHash? hashB = MediaScanService.BuildDHash(inverted);

        Assert.True(hashA.HasValue);
        Assert.True(hashB.HasValue);
        Assert.Equal(hashA.Value, MediaScanService.BuildDHash(checker)!.Value);
        Assert.True(
            MediaScanService.HammingDistance(hashA.Value, hashB.Value) >
            MediaDeduplicationService.MaxHammingDistanceForSimilar);
    }

    private static byte[] CreateBgraBuffer(Func<int, int, byte> gray)
    {
        const int width = 33;
        const int height = 33;
        byte[] buffer = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                byte value = gray(x, y);
                buffer[offset] = value;     // B
                buffer[offset + 1] = value; // G
                buffer[offset + 2] = value; // R
                buffer[offset + 3] = 255;   // A
            }
        }

        return buffer;
    }

    private static string GetTestDataPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidatePath = Path.Combine(directory.FullName, "TestData", "MediaRealWorld", fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Test data file was not found: {fileName}");
    }
}
