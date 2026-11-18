#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class PreviewController
{
    private Task<string> folderPicker;

    private void HandleWindowsInput()
    {
#if !UNITY_EDITOR
        WindowsPreviewInput.EnableFileDrop();
        if (WindowsPreviewInput.TryTakeDrop(out var droppedPath))
        {
            if (Directory.Exists(droppedPath)) OpenResourceDirectory(droppedPath);
            else loadError = "请一次拖入一个 Spine 资源文件夹。";
        }
#endif

        if (folderPicker == null || !folderPicker.IsCompleted) return;
        var completed = folderPicker;
        folderPicker = null;
        try
        {
            var selected = completed.GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(selected)) OpenResourceDirectory(selected);
        }
        catch (Exception exception)
        {
            loadError = "无法打开文件夹选择窗口：" + exception.Message;
            Debug.LogException(exception);
        }
    }

    private void OpenResourceDirectory(string directory)
    {
        resourceDirectory = directory;
        reloadRequested = false;
        LoadResource();
    }
}
#endif
