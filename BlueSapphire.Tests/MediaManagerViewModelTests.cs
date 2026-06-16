using Microsoft.Extensions.Logging.Abstractions;
using BlueSapphire.Services;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Tests;

public class MediaManagerViewModelTests
{
    private MediaManagerViewModel CreateViewModel()
    {
        return new MediaManagerViewModel(
            new MediaRenameService(),
            new MediaDeduplicationService(NullLogger<MediaDeduplicationService>.Instance),
            new NativeFileService(),
            new ImageProcessingService(),
            new ImageMetadataService(),
            new MediaTagService(NullLogger<MediaTagService>.Instance),
            NullLogger<MediaManagerViewModel>.Instance);
    }

    // ================================================================
    // Test 1: NormalizeFolderPathInput 空输入返回 null
    // ================================================================
    [Fact]
    public void NormalizeFolderPathInput_ReturnsNull_ForEmptyInput()
    {
        Assert.Null(InvokeNormalizeFolderPathInput("   "));
    }

    // ================================================================
    // Test 2: NormalizeFolderPathInput 展开环境变量并去除引号
    // ================================================================
    [Fact]
    public void NormalizeFolderPathInput_ExpandsEnvironmentVariables_AndTrimsQuotes()
    {
        string expected = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string input = "\"%LOCALAPPDATA%\"";

        string? actual = InvokeNormalizeFolderPathInput(input);

        Assert.Equal(expected, actual);
    }

    // ================================================================
    // Test 3: 构造函数成功创建实例
    // ================================================================
    [Fact]
    public void Constructor_CreatesViewModelSuccessfully()
    {
        MediaManagerViewModel vm = CreateViewModel();

        Assert.NotNull(vm);
    }

    // ================================================================
    // Test 4: 初始状态下图片工作区不可见
    // ================================================================
    [Fact]
    public void InitialState_ImageWorkspaceIsHidden()
    {
        MediaManagerViewModel vm = CreateViewModel();

        Assert.False(vm.IsImageWorkspaceVisible);
    }

    // ================================================================
    // Test 5: OpenImageWorkspace 显示工作区
    // ================================================================
    [Fact]
    public void OpenImageWorkspace_SetsVisibleToTrue()
    {
        MediaManagerViewModel vm = CreateViewModel();

        vm.OpenImageWorkspaceCommand.Execute(null);

        Assert.True(vm.IsImageWorkspaceVisible);
    }

    // ================================================================
    // Test 6: ReturnToMediaHome 隐藏工作区
    // ================================================================
    [Fact]
    public void ReturnToMediaHome_SetsVisibleToFalse()
    {
        MediaManagerViewModel vm = CreateViewModel();
        vm.OpenImageWorkspaceCommand.Execute(null);

        vm.ReturnToMediaHomeCommand.Execute(null);

        Assert.False(vm.IsImageWorkspaceVisible);
    }

    // ================================================================
    // Test 7: 构造后图片列表为空
    // ================================================================
    [Fact]
    public void InitialState_ImageCollectionIsEmpty()
    {
        MediaManagerViewModel vm = CreateViewModel();

        Assert.Empty(vm.Images);
    }

    private static string? InvokeNormalizeFolderPathInput(string? input)
    {
        System.Reflection.MethodInfo method = typeof(MediaManagerViewModel).GetMethod(
            "NormalizeFolderPathInput",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        return (string?)method.Invoke(null, [input]);
    }
}
