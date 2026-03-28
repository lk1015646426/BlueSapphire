using BlueSapphire.Models;
using BlueSapphire.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace BlueSapphire.Tests;

public class PdfDocumentServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PdfDocumentService _service = new();

    public PdfDocumentServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task MergePdfFilesAsync_CreatesMergedPdf()
    {
        var sourceA = CreatePdf("a.pdf", 1);
        var sourceB = CreatePdf("b.pdf", 2);

        var result = await _service.MergePdfFilesAsync(new[] { sourceA, sourceB });

        Assert.Equal(DocumentConversionBatchItemStatus.Succeeded, result.Status);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        using var merged = PdfReader.Open(result.OutputPath!, PdfDocumentOpenMode.Import);
        Assert.Equal(3, merged.PageCount);
    }

    [Fact]
    public async Task SplitPdfFileAsync_CreatesPerPagePdfFiles()
    {
        var source = CreatePdf("source.pdf", 3);

        var result = await _service.SplitPdfFileAsync(source);

        Assert.Equal(DocumentConversionBatchItemStatus.Succeeded, result.Status);
        Assert.NotNull(result.OutputPath);
        Assert.True(Directory.Exists(result.OutputPath));
        Assert.Equal(3, Directory.GetFiles(result.OutputPath!, "*.pdf").Length);
    }

    [Fact]
    public async Task ExtractPagesAsync_CreatesPdfWithRequestedPages()
    {
        var source = CreatePdf("source.pdf", 5);

        var result = await _service.ExtractPagesAsync(source, "2-3,5");

        Assert.Equal(DocumentConversionBatchItemStatus.Succeeded, result.Status);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        using var extracted = PdfReader.Open(result.OutputPath!, PdfDocumentOpenMode.Import);
        Assert.Equal(3, extracted.PageCount);
    }

    [Fact]
    public void ParsePageSelection_SupportsRangesAndChineseComma()
    {
        var result = _service.ParsePageSelection("1-2，4,6-7", 10);

        Assert.Equal(new[] { 1, 2, 4, 6, 7 }, result);
    }

    [Fact]
    public void ParsePageSelection_ThrowsWhenPageOutOfRange()
    {
        Assert.Throws<ArgumentException>(() => _service.ParsePageSelection("1-5", 3));
    }

    private string CreatePdf(string fileName, int pageCount)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        using var document = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }

        document.Save(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
