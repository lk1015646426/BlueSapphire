using BlueSapphire.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public class PdfDocumentService
    {
        public bool IsPdf(string? fileName)
        {
            return string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<DocumentConversionBatchItem> MergePdfFilesAsync(
            IReadOnlyList<string> sourcePaths,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                string primarySourcePath = sourcePaths.FirstOrDefault() ?? string.Empty;

                if (sourcePaths.Count < 2)
                {
                    return new DocumentConversionBatchItem(
                        primarySourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        "至少需要选择 2 个 PDF 文件才能合并。");
                }

                string outputDirectory = ResolveSharedOutputDirectory(sourcePaths);
                string outputPath = BuildMergedOutputPath(outputDirectory);

                try
                {
                    using var output = new PdfDocument();

                    foreach (string sourcePath in sourcePaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!File.Exists(sourcePath))
                        {
                            return new DocumentConversionBatchItem(
                                primarySourcePath,
                                null,
                                DocumentConversionBatchItemStatus.Failed,
                                "待合并的 PDF 文件不存在。");
                        }

                        using var input = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                        foreach (PdfPage page in input.Pages)
                        {
                            output.AddPage(page);
                        }
                    }

                    output.Save(outputPath);

                    return new DocumentConversionBatchItem(
                        primarySourcePath,
                        outputPath,
                        DocumentConversionBatchItemStatus.Succeeded,
                        $"已合并 {sourcePaths.Count} 个 PDF 文件。",
                        DocumentOperationResultTargetKind.File);
                }
                catch (OperationCanceledException)
                {
                    return new DocumentConversionBatchItem(
                        primarySourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        "合并已取消。");
                }
                catch (Exception ex)
                {
                    return new DocumentConversionBatchItem(
                        primarySourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Failed,
                        ex.Message);
                }
            }, cancellationToken);
        }

        public async Task<DocumentConversionBatchItem> SplitPdfFileAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    return new DocumentConversionBatchItem(
                        sourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        "源 PDF 不存在。");
                }

                string outputDirectory = BuildSplitOutputDirectory(sourcePath);

                try
                {
                    Directory.CreateDirectory(outputDirectory);

                    using var input = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < input.PageCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        using var output = new PdfDocument();
                        output.AddPage(input.Pages[i]);
                        string pagePath = Path.Combine(outputDirectory, $"page_{i + 1:D3}.pdf");
                        output.Save(pagePath);
                    }

                    return new DocumentConversionBatchItem(
                        sourcePath,
                        outputDirectory,
                        DocumentConversionBatchItemStatus.Succeeded,
                        $"已拆分 {input.PageCount} 页。",
                        DocumentOperationResultTargetKind.Folder);
                }
                catch (OperationCanceledException)
                {
                    return new DocumentConversionBatchItem(
                        sourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        "拆分已取消。");
                }
                catch (Exception ex)
                {
                    return new DocumentConversionBatchItem(
                        sourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Failed,
                        ex.Message);
                }
            }, cancellationToken);
        }

        public async Task<DocumentConversionBatchItem> ExtractPagesAsync(
            string sourcePath,
            string pageSelectionText,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    return new DocumentConversionBatchItem(
                        sourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        "源 PDF 不存在。");
                }

                try
                {
                    using var input = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                    var selectedPages = ParsePageSelection(pageSelectionText, input.PageCount);
                    string outputPath = BuildExtractOutputPath(sourcePath);

                    using var output = new PdfDocument();
                    foreach (int pageNumber in selectedPages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.AddPage(input.Pages[pageNumber - 1]);
                    }

                    output.Save(outputPath);

                    return new DocumentConversionBatchItem(
                        sourcePath,
                        outputPath,
                        DocumentConversionBatchItemStatus.Succeeded,
                        $"已提取 {selectedPages.Count} 页：{string.Join(", ", selectedPages)}。",
                        DocumentOperationResultTargetKind.File);
                }
                catch (OperationCanceledException)
                {
                    return new DocumentConversionBatchItem(
                        sourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        "页面提取已取消。");
                }
                catch (Exception ex)
                {
                    return new DocumentConversionBatchItem(
                        sourcePath,
                        null,
                        DocumentConversionBatchItemStatus.Failed,
                        ex.Message);
                }
            }, cancellationToken);
        }

        public IReadOnlyList<int> ParsePageSelection(string pageSelectionText, int pageCount)
        {
            if (pageCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCount), "PDF 页数必须大于 0。");
            }

            if (string.IsNullOrWhiteSpace(pageSelectionText))
            {
                throw new ArgumentException("请输入页码范围，例如 1-3,5。", nameof(pageSelectionText));
            }

            var selectedPages = new SortedSet<int>();
            var segments = pageSelectionText
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string segment in segments)
            {
                var parts = segment.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 1)
                {
                    selectedPages.Add(ParsePageNumber(parts[0], pageCount));
                    continue;
                }

                if (parts.Length != 2)
                {
                    throw new ArgumentException($"无法识别页码范围：{segment}", nameof(pageSelectionText));
                }

                int start = ParsePageNumber(parts[0], pageCount);
                int end = ParsePageNumber(parts[1], pageCount);
                if (end < start)
                {
                    throw new ArgumentException($"页码范围无效：{segment}", nameof(pageSelectionText));
                }

                for (int page = start; page <= end; page++)
                {
                    selectedPages.Add(page);
                }
            }

            if (selectedPages.Count == 0)
            {
                throw new ArgumentException("未解析到有效页码。", nameof(pageSelectionText));
            }

            return selectedPages.ToList();
        }

        private static string ResolveSharedOutputDirectory(IReadOnlyList<string> sourcePaths)
        {
            string firstDirectory = Path.GetDirectoryName(sourcePaths[0]) ?? string.Empty;
            bool isSameDirectory = sourcePaths.All(path =>
                string.Equals(Path.GetDirectoryName(path), firstDirectory, StringComparison.OrdinalIgnoreCase));

            return isSameDirectory
                ? firstDirectory
                : Path.GetDirectoryName(sourcePaths[0]) ?? Environment.CurrentDirectory;
        }

        private static string BuildMergedOutputPath(string directory)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = $"merged_{timestamp}";
            string outputPath = Path.Combine(directory, baseName + ".pdf");
            int counter = 1;

            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(directory, $"{baseName}_{counter:D2}.pdf");
                counter++;
            }

            return outputPath;
        }

        private static string BuildSplitOutputDirectory(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(sourcePath) + "_split";
            string outputDirectory = Path.Combine(directory, baseName);
            int counter = 1;

            while (Directory.Exists(outputDirectory))
            {
                outputDirectory = Path.Combine(directory, $"{baseName}_{counter:D2}");
                counter++;
            }

            return outputDirectory;
        }

        private static string BuildExtractOutputPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(sourcePath) + "_extract";
            string outputPath = Path.Combine(directory, baseName + ".pdf");
            int counter = 1;

            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(directory, $"{baseName}_{counter:D2}.pdf");
                counter++;
            }

            return outputPath;
        }

        private static int ParsePageNumber(string value, int pageCount)
        {
            if (!int.TryParse(value, out int pageNumber) || pageNumber < 1 || pageNumber > pageCount)
            {
                throw new ArgumentException($"页码超出范围：{value}，有效范围是 1-{pageCount}。", nameof(value));
            }

            return pageNumber;
        }
    }
}
