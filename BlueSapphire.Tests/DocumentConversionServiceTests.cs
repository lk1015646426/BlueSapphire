using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class DocumentConversionServiceTests
{
    private readonly DocumentConversionService _service = new();

    [Theory]
    [InlineData("report.docx")]
    [InlineData("report.docm")]
    [InlineData("notes.rtf")]
    [InlineData("sheet.xlsx")]
    [InlineData("sheet.csv")]
    [InlineData("deck.pptm")]
    [InlineData("notes.txt")]
    public void CanConvertToPdf_ReturnsTrueForSupportedSourceTypes(string fileName)
    {
        Assert.True(_service.CanConvertToPdf(fileName));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("archive.zip")]
    [InlineData("")]
    [InlineData(null)]
    public void CanConvertToPdf_ReturnsFalseForUnsupportedSourceTypes(string? fileName)
    {
        Assert.False(_service.CanConvertToPdf(fileName));
    }

    [Theory]
    [InlineData("report.docx", DocumentConversionTarget.Doc)]
    [InlineData("report.pdf", DocumentConversionTarget.Docx)]
    [InlineData("sheet.xlsx", DocumentConversionTarget.Csv)]
    [InlineData("deck.pptx", DocumentConversionTarget.Ppt)]
    public void CanConvertToTarget_ReturnsTrueForSupportedCombinations(string fileName, DocumentConversionTarget target)
    {
        Assert.True(_service.CanConvertToTarget(fileName, target));
    }

    [Theory]
    [InlineData("report.pdf", DocumentConversionTarget.Xlsx)]
    [InlineData("sheet.xlsx", DocumentConversionTarget.Pptx)]
    [InlineData("deck.pptx", DocumentConversionTarget.Docx)]
    [InlineData("archive.zip", DocumentConversionTarget.Pdf)]
    public void CanConvertToTarget_ReturnsFalseForUnsupportedCombinations(string fileName, DocumentConversionTarget target)
    {
        Assert.False(_service.CanConvertToTarget(fileName, target));
    }
}
