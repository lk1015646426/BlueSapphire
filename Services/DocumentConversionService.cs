using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public enum DocumentConversionTarget
    {
        Pdf,
        Docx,
        Doc,
        Rtf,
        Txt,
        Xlsx,
        Xls,
        Csv,
        Pptx,
        Ppt
    }

    public class DocumentConversionService
    {
        private static readonly string[] WordProgIds = { "Word.Application", "KWPS.Application" };
        private static readonly string[] ExcelProgIds = { "Excel.Application", "KET.Application" };
        private static readonly string[] PowerPointProgIds = { "PowerPoint.Application", "KWPP.Application" };

        private readonly object _environmentLock = new();
        private Task<DocumentConversionEnvironmentStatus>? _environmentStatusTask;

        public bool CanConvertToPdf(string? fileName)
        {
            return CanConvertToTarget(fileName, DocumentConversionTarget.Pdf);
        }

        public bool CanConvertToTarget(string? fileName, DocumentConversionTarget target)
        {
            return SupportsTarget(GetSourceKind(fileName), target);
        }

        public bool TryParseTarget(string? targetKey, out DocumentConversionTarget target)
        {
            return Enum.TryParse(targetKey, ignoreCase: true, out target);
        }

        public string GetTargetDisplayName(DocumentConversionTarget target)
        {
            return target switch
            {
                DocumentConversionTarget.Pdf => "PDF",
                DocumentConversionTarget.Docx => "Word (DOCX)",
                DocumentConversionTarget.Doc => "Word (DOC)",
                DocumentConversionTarget.Rtf => "富文本 (RTF)",
                DocumentConversionTarget.Txt => "纯文本 (TXT)",
                DocumentConversionTarget.Xlsx => "Excel (XLSX)",
                DocumentConversionTarget.Xls => "Excel (XLS)",
                DocumentConversionTarget.Csv => "CSV",
                DocumentConversionTarget.Pptx => "PowerPoint (PPTX)",
                DocumentConversionTarget.Ppt => "PowerPoint (PPT)",
                _ => target.ToString().ToUpperInvariant()
            };
        }

        public string GetTargetExtension(DocumentConversionTarget target)
        {
            return target switch
            {
                DocumentConversionTarget.Pdf => ".pdf",
                DocumentConversionTarget.Docx => ".docx",
                DocumentConversionTarget.Doc => ".doc",
                DocumentConversionTarget.Rtf => ".rtf",
                DocumentConversionTarget.Txt => ".txt",
                DocumentConversionTarget.Xlsx => ".xlsx",
                DocumentConversionTarget.Xls => ".xls",
                DocumentConversionTarget.Csv => ".csv",
                DocumentConversionTarget.Pptx => ".pptx",
                DocumentConversionTarget.Ppt => ".ppt",
                _ => string.Empty
            };
        }

        public bool IsConversionAvailable(
            string? fileName,
            DocumentConversionTarget target,
            DocumentConversionEnvironmentStatus environmentStatus)
        {
            return GetRequiredCapability(GetSourceKind(fileName), target) switch
            {
                DocumentAutomationCapability.Word => environmentStatus.Word.IsAvailable,
                DocumentAutomationCapability.Excel => environmentStatus.Excel.IsAvailable,
                DocumentAutomationCapability.PowerPoint => environmentStatus.PowerPoint.IsAvailable,
                _ => false
            };
        }

        public string GetRequiredCapabilityDisplayName(string? fileName, DocumentConversionTarget target)
        {
            return GetRequiredCapability(GetSourceKind(fileName), target) switch
            {
                DocumentAutomationCapability.Word => "Word / WPS 文字",
                DocumentAutomationCapability.Excel => "Excel / WPS 表格",
                DocumentAutomationCapability.PowerPoint => "PowerPoint / WPS 演示",
                _ => "文档转换引擎"
            };
        }

        public Task<DocumentConversionEnvironmentStatus> GetEnvironmentStatusAsync(bool forceRefresh = false)
        {
            lock (_environmentLock)
            {
                if (forceRefresh || _environmentStatusTask == null)
                {
                    _environmentStatusTask = ProbeEnvironmentAsync();
                }

                return _environmentStatusTask;
            }
        }

        public Task<DocumentConversionResult> ConvertToPdfAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            return ConvertAsync(sourcePath, DocumentConversionTarget.Pdf, cancellationToken);
        }

        public async Task<DocumentConversionResult> ConvertAsync(
            string sourcePath,
            DocumentConversionTarget target,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return DocumentConversionResult.Failed(sourcePath, "源文件不存在。");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return DocumentConversionResult.Failed(sourcePath, "转换已取消。");
            }

            string targetExtension = GetTargetExtension(target);
            if (string.Equals(Path.GetExtension(sourcePath), targetExtension, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConversionResult.Failed(sourcePath, "源文件已经是目标格式。");
            }

            var sourceKind = GetSourceKind(sourcePath);
            if (!SupportsTarget(sourceKind, target))
            {
                return DocumentConversionResult.Failed(
                    sourcePath,
                    $"当前文件不支持转换为 {GetTargetDisplayName(target)}。");
            }

            string outputPath = BuildOutputPath(sourcePath, targetExtension);

            return sourceKind switch
            {
                DocumentSourceKind.Word => await RunStaAsync(() => ConvertWithWord(sourcePath, outputPath, target)),
                DocumentSourceKind.Excel => await RunStaAsync(() => ConvertWithExcel(sourcePath, outputPath, target)),
                DocumentSourceKind.PowerPoint => await RunStaAsync(() => ConvertWithPowerPoint(sourcePath, outputPath, target)),
                DocumentSourceKind.Pdf => await RunStaAsync(() => ConvertWithWord(sourcePath, outputPath, target)),
                _ => DocumentConversionResult.Failed(sourcePath, "暂不支持此文件类型的格式转换。")
            };
        }

        private static DocumentConversionResult ConvertWithWord(
            string sourcePath,
            string outputPath,
            DocumentConversionTarget target)
        {
            object? wordApp = null;
            object? documents = null;
            object? document = null;

            try
            {
                var wordType = ResolveAutomationType(WordProgIds, out _);
                if (wordType == null)
                {
                    return DocumentConversionResult.Failed(sourcePath, "未检测到可用的 Word / WPS 文字 自动化环境。");
                }

                dynamic app = Activator.CreateInstance(wordType)!;
                wordApp = app;
                app.Visible = false;
                app.DisplayAlerts = 0;

                documents = app.Documents;
                document = ((dynamic)documents).Open(sourcePath, ReadOnly: true, AddToRecentFiles: false, Visible: false);

                switch (target)
                {
                    case DocumentConversionTarget.Pdf:
                        ((dynamic)document).ExportAsFixedFormat(outputPath, 17);
                        break;
                    case DocumentConversionTarget.Docx:
                        SaveWithFallback(document, outputPath, 16);
                        break;
                    case DocumentConversionTarget.Doc:
                        SaveWithFallback(document, outputPath, 0);
                        break;
                    case DocumentConversionTarget.Rtf:
                        SaveWithFallback(document, outputPath, 6);
                        break;
                    case DocumentConversionTarget.Txt:
                        SaveWithFallback(document, outputPath, 2);
                        break;
                    default:
                        return DocumentConversionResult.Failed(sourcePath, "当前文字引擎不支持该目标格式。");
                }

                return DocumentConversionResult.Succeeded(sourcePath, outputPath);
            }
            catch (Exception ex)
            {
                return DocumentConversionResult.Failed(sourcePath, ex.Message);
            }
            finally
            {
                TryClose(document, false);
                TryQuit(wordApp);
                SafeRelease(document);
                SafeRelease(documents);
                SafeRelease(wordApp);
            }
        }

        private static DocumentConversionResult ConvertWithExcel(
            string sourcePath,
            string outputPath,
            DocumentConversionTarget target)
        {
            object? excelApp = null;
            object? workbooks = null;
            object? workbook = null;

            try
            {
                var excelType = ResolveAutomationType(ExcelProgIds, out _);
                if (excelType == null)
                {
                    return DocumentConversionResult.Failed(sourcePath, "未检测到可用的 Excel / WPS 表格 自动化环境。");
                }

                dynamic app = Activator.CreateInstance(excelType)!;
                excelApp = app;
                app.Visible = false;
                app.DisplayAlerts = false;

                workbooks = app.Workbooks;
                workbook = ((dynamic)workbooks).Open(sourcePath, ReadOnly: true);

                switch (target)
                {
                    case DocumentConversionTarget.Pdf:
                        ((dynamic)workbook).ExportAsFixedFormat(0, outputPath);
                        break;
                    case DocumentConversionTarget.Xlsx:
                        SaveWithFallback(workbook, outputPath, 51);
                        break;
                    case DocumentConversionTarget.Xls:
                        SaveWithFallback(workbook, outputPath, -4143);
                        break;
                    case DocumentConversionTarget.Csv:
                        SaveWithFallback(workbook, outputPath, 6);
                        break;
                    default:
                        return DocumentConversionResult.Failed(sourcePath, "当前表格引擎不支持该目标格式。");
                }

                return DocumentConversionResult.Succeeded(sourcePath, outputPath);
            }
            catch (Exception ex)
            {
                return DocumentConversionResult.Failed(sourcePath, ex.Message);
            }
            finally
            {
                TryClose(workbook, false);
                TryQuit(excelApp);
                SafeRelease(workbook);
                SafeRelease(workbooks);
                SafeRelease(excelApp);
            }
        }

        private static DocumentConversionResult ConvertWithPowerPoint(
            string sourcePath,
            string outputPath,
            DocumentConversionTarget target)
        {
            object? powerPointApp = null;
            object? presentations = null;
            object? presentation = null;

            try
            {
                var powerPointType = ResolveAutomationType(PowerPointProgIds, out _);
                if (powerPointType == null)
                {
                    return DocumentConversionResult.Failed(sourcePath, "未检测到可用的 PowerPoint / WPS 演示 自动化环境。");
                }

                dynamic app = Activator.CreateInstance(powerPointType)!;
                powerPointApp = app;
                TrySetProperty(powerPointApp, "Visible", false);
                presentations = app.Presentations;
                presentation = ((dynamic)presentations).Open(sourcePath, ReadOnly: 1, Untitled: 0, WithWindow: 0);

                switch (target)
                {
                    case DocumentConversionTarget.Pdf:
                        ((dynamic)presentation).SaveAs(outputPath, 32);
                        break;
                    case DocumentConversionTarget.Pptx:
                        ((dynamic)presentation).SaveAs(outputPath, 24);
                        break;
                    case DocumentConversionTarget.Ppt:
                        ((dynamic)presentation).SaveAs(outputPath, 1);
                        break;
                    default:
                        return DocumentConversionResult.Failed(sourcePath, "当前演示引擎不支持该目标格式。");
                }

                return DocumentConversionResult.Succeeded(sourcePath, outputPath);
            }
            catch (Exception ex)
            {
                return DocumentConversionResult.Failed(sourcePath, ex.Message);
            }
            finally
            {
                TryClose(presentation);
                TryQuit(powerPointApp);
                SafeRelease(presentation);
                SafeRelease(presentations);
                SafeRelease(powerPointApp);
            }
        }

        private async Task<DocumentConversionEnvironmentStatus> ProbeEnvironmentAsync()
        {
            var word = await ProbeApplicationAsync("Word 文档", WordProgIds);
            var excel = await ProbeApplicationAsync("Excel 表格", ExcelProgIds);
            var powerPoint = await ProbeApplicationAsync("PowerPoint 演示", PowerPointProgIds);

            return new DocumentConversionEnvironmentStatus(word, excel, powerPoint);
        }

        private static Task<DocumentAutomationProbeResult> ProbeApplicationAsync(string capabilityName, string[] progIds)
        {
            return RunStaAsync(() => ProbeApplication(capabilityName, progIds));
        }

        private static DocumentAutomationProbeResult ProbeApplication(string capabilityName, string[] progIds)
        {
            object? app = null;

            try
            {
                var appType = ResolveAutomationType(progIds, out string? resolvedProgId);
                if (appType == null)
                {
                    return DocumentAutomationProbeResult.Unavailable(capabilityName, "未找到可用的自动化接口。");
                }

                app = Activator.CreateInstance(appType);
                string providerName = TryGetStringProperty(app, "Name") ?? resolvedProgId ?? capabilityName;
                TrySetProperty(app, "Visible", false);
                TryQuit(app);

                return DocumentAutomationProbeResult.Available(capabilityName, providerName, resolvedProgId);
            }
            catch (Exception ex)
            {
                return DocumentAutomationProbeResult.Unavailable(capabilityName, ex.Message);
            }
            finally
            {
                TryQuit(app);
                SafeRelease(app);
            }
        }

        private static Task<T> RunStaAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }

        private static Type? ResolveAutomationType(string[] progIds, out string? resolvedProgId)
        {
            foreach (string progId in progIds)
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: false);
                if (type != null)
                {
                    resolvedProgId = progId;
                    return type;
                }
            }

            resolvedProgId = null;
            return null;
        }

        private static void SaveWithFallback(object? comObject, string outputPath, int fileFormat)
        {
            if (comObject == null)
            {
                throw new InvalidOperationException("未找到可用的文档对象。");
            }

            if (TryInvokeMember(comObject, "SaveAs2", outputPath, fileFormat))
            {
                return;
            }

            if (TryInvokeMember(comObject, "SaveAs", outputPath, fileFormat))
            {
                return;
            }

            throw new InvalidOperationException("当前自动化接口不支持该格式的保存操作。");
        }

        private static bool SupportsTarget(DocumentSourceKind sourceKind, DocumentConversionTarget target)
        {
            return sourceKind switch
            {
                DocumentSourceKind.Word => target is DocumentConversionTarget.Pdf
                    or DocumentConversionTarget.Docx
                    or DocumentConversionTarget.Doc
                    or DocumentConversionTarget.Rtf
                    or DocumentConversionTarget.Txt,
                DocumentSourceKind.Excel => target is DocumentConversionTarget.Pdf
                    or DocumentConversionTarget.Xlsx
                    or DocumentConversionTarget.Xls
                    or DocumentConversionTarget.Csv,
                DocumentSourceKind.PowerPoint => target is DocumentConversionTarget.Pdf
                    or DocumentConversionTarget.Pptx
                    or DocumentConversionTarget.Ppt,
                DocumentSourceKind.Pdf => target is DocumentConversionTarget.Docx
                    or DocumentConversionTarget.Doc,
                _ => false
            };
        }

        private static DocumentAutomationCapability GetRequiredCapability(
            DocumentSourceKind sourceKind,
            DocumentConversionTarget target)
        {
            return sourceKind switch
            {
                DocumentSourceKind.Word => DocumentAutomationCapability.Word,
                DocumentSourceKind.Excel => DocumentAutomationCapability.Excel,
                DocumentSourceKind.PowerPoint => DocumentAutomationCapability.PowerPoint,
                DocumentSourceKind.Pdf when target is DocumentConversionTarget.Docx or DocumentConversionTarget.Doc
                    => DocumentAutomationCapability.Word,
                _ => DocumentAutomationCapability.Unsupported
            };
        }

        private static DocumentSourceKind GetSourceKind(string? fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

            return extension switch
            {
                ".doc" => DocumentSourceKind.Word,
                ".docx" => DocumentSourceKind.Word,
                ".docm" => DocumentSourceKind.Word,
                ".rtf" => DocumentSourceKind.Word,
                ".txt" => DocumentSourceKind.Word,
                ".xls" => DocumentSourceKind.Excel,
                ".xlsx" => DocumentSourceKind.Excel,
                ".xlsm" => DocumentSourceKind.Excel,
                ".xlsb" => DocumentSourceKind.Excel,
                ".csv" => DocumentSourceKind.Excel,
                ".ppt" => DocumentSourceKind.PowerPoint,
                ".pptx" => DocumentSourceKind.PowerPoint,
                ".pptm" => DocumentSourceKind.PowerPoint,
                ".pdf" => DocumentSourceKind.Pdf,
                _ => DocumentSourceKind.Unsupported
            };
        }

        private static string BuildOutputPath(string sourcePath, string targetExtension)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(directory, baseName + targetExtension);

            if (!File.Exists(outputPath))
            {
                return outputPath;
            }

            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(directory, $"{baseName}_{counter:D2}{targetExtension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }

                counter++;
            }
        }

        private static bool TryInvokeMember(object comObject, string methodName, params object[] args)
        {
            try
            {
                comObject.GetType().InvokeMember(
                    methodName,
                    BindingFlags.InvokeMethod,
                    null,
                    comObject,
                    args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryClose(object? comObject, params object[] args)
        {
            if (comObject == null)
            {
                return;
            }

            TryInvokeMember(comObject, "Close", args);
        }

        private static void TryQuit(object? comObject)
        {
            if (comObject == null)
            {
                return;
            }

            TryInvokeMember(comObject, "Quit", Array.Empty<object>());
        }

        private static void TrySetProperty(object? comObject, string propertyName, object value)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                comObject.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.SetProperty,
                    null,
                    comObject,
                    new[] { value });
            }
            catch
            {
            }
        }

        private static string? TryGetStringProperty(object? comObject, string propertyName)
        {
            if (comObject == null)
            {
                return null;
            }

            try
            {
                return comObject.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.GetProperty,
                    null,
                    comObject,
                    Array.Empty<object>()) as string;
            }
            catch
            {
                return null;
            }
        }

        private static void SafeRelease(object? comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
            {
                return;
            }

            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
            }
        }

        private enum DocumentSourceKind
        {
            Unsupported,
            Word,
            Excel,
            PowerPoint,
            Pdf
        }

        private enum DocumentAutomationCapability
        {
            Unsupported,
            Word,
            Excel,
            PowerPoint
        }
    }

    public sealed record DocumentConversionEnvironmentStatus(
        DocumentAutomationProbeResult Word,
        DocumentAutomationProbeResult Excel,
        DocumentAutomationProbeResult PowerPoint)
    {
        public bool IsAnyAvailable => Word.IsAvailable || Excel.IsAvailable || PowerPoint.IsAvailable;

        public bool IsFullyAvailable => Word.IsAvailable && Excel.IsAvailable && PowerPoint.IsAvailable;

        public string ShortText => !IsAnyAvailable
            ? "文档引擎：不可用"
            : IsFullyAvailable
                ? "文档引擎：已就绪"
                : "文档引擎：部分可用";

        public string DetailText
        {
            get
            {
                return string.Join("\n", new[]
                {
                    BuildLine("Word", Word),
                    BuildLine("Excel", Excel),
                    BuildLine("PowerPoint", PowerPoint)
                });
            }
        }

        private static string BuildLine(string label, DocumentAutomationProbeResult probe)
        {
            return probe.IsAvailable
                ? $"{label}: 可用（{probe.ProviderName ?? probe.ProgId ?? "兼容接口"}）"
                : $"{label}: 不可用（{probe.Message}）";
        }
    }

    public sealed record DocumentAutomationProbeResult(
        string CapabilityName,
        bool IsAvailable,
        string? ProviderName,
        string? ProgId,
        string Message)
    {
        public static DocumentAutomationProbeResult Available(string capabilityName, string? providerName, string? progId)
        {
            return new DocumentAutomationProbeResult(capabilityName, true, providerName, progId, "自动化接口可用。");
        }

        public static DocumentAutomationProbeResult Unavailable(string capabilityName, string message)
        {
            return new DocumentAutomationProbeResult(capabilityName, false, null, null, message);
        }
    }

    public sealed record DocumentConversionResult(string SourcePath, string? OutputPath, bool Success, string Message)
    {
        public static DocumentConversionResult Succeeded(string sourcePath, string outputPath)
        {
            return new DocumentConversionResult(sourcePath, outputPath, true, "转换成功。");
        }

        public static DocumentConversionResult Failed(string sourcePath, string message)
        {
            return new DocumentConversionResult(sourcePath, null, false, message);
        }
    }
}
