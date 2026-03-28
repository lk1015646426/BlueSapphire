using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class DocumentConversionBatchReportTests
{
    [Fact]
    public void SummaryProperties_CountStatusesCorrectly()
    {
        var report = new DocumentConversionBatchReport(new[]
        {
            new DocumentConversionBatchItem(@"C:\docs\a.docx", @"C:\docs\a.pdf", DocumentConversionBatchItemStatus.Succeeded, "转换成功。"),
            new DocumentConversionBatchItem(@"C:\docs\b.pdf", null, DocumentConversionBatchItemStatus.Skipped, "文件已是 PDF。"),
            new DocumentConversionBatchItem(@"C:\docs\c.docx", null, DocumentConversionBatchItemStatus.Failed, "转换失败。")
        });

        Assert.Equal(3, report.TotalCount);
        Assert.Equal(1, report.SuccessCount);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal("有失败项", report.HistoryStatusText);
    }

    [Fact]
    public void CreatedAtText_UsesProvidedTimestamp()
    {
        var createdAt = new DateTimeOffset(2026, 3, 28, 21, 30, 0, TimeSpan.FromHours(8));
        var report = new DocumentConversionBatchReport(
            Array.Empty<DocumentConversionBatchItem>(),
            operationName: "PDF 页面提取",
            dialogTitle: "结果",
            createdAt: createdAt);

        Assert.Equal("PDF 页面提取", report.OperationName);
        Assert.Equal("2026-03-28 21:30:00", report.CreatedAtText);
        Assert.Equal("未执行", report.HistoryStatusText);
    }
}
