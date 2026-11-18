#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class WindowsPreviewInput
{
    private const uint WmDropFiles = 0x0233;
    private const uint WmNcDestroy = 0x0082;
    private const uint CountDroppedItems = uint.MaxValue;
    private static readonly UIntPtr DropSubclassId = new UIntPtr(1);
    private static readonly SubclassProc DropProc = ReceiveDrop;
    private static IntPtr dropWindow;
    private static string droppedPath;

    public static void EnableFileDrop()
    {
        if (dropWindow != IntPtr.Zero) return;
        var activeWindow = GetActiveWindow();
        if (activeWindow == IntPtr.Zero || !SetWindowSubclass(activeWindow, DropProc, DropSubclassId, UIntPtr.Zero)) return;
        dropWindow = activeWindow;
        DragAcceptFiles(dropWindow, true);
    }

    public static bool TryTakeDrop(out string path)
    {
        path = droppedPath;
        droppedPath = null;
        return path != null;
    }

    public static void StopFileDrop()
    {
        if (dropWindow == IntPtr.Zero) return;
        DragAcceptFiles(dropWindow, false);
        RemoveWindowSubclass(dropWindow, DropProc, DropSubclassId);
        dropWindow = IntPtr.Zero;
        droppedPath = null;
    }

    [AOT.MonoPInvokeCallback(typeof(SubclassProc))]
    private static IntPtr ReceiveDrop(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == WmDropFiles)
        {
            try
            {
                droppedPath = string.Empty;
                if (DragQueryFile(wParam, CountDroppedItems, null, 0) == 1)
                {
                    var capacity = DragQueryFile(wParam, 0, null, 0) + 1;
                    var buffer = new StringBuilder((int)capacity);
                    if (DragQueryFile(wParam, 0, buffer, capacity) > 0) droppedPath = buffer.ToString();
                }
            }
            finally
            {
                DragFinish(wParam);
            }
            return IntPtr.Zero;
        }

        if (message == WmNcDestroy) StopFileDrop();
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public static Task<string> PickFolderAsync(string currentDirectory)
    {
        var completion = new TaskCompletionSource<string>();
        var owner = GetActiveWindow();
        // The dialog needs an STA, and the player thread must keep pumping the owner window.
        var thread = new Thread(() =>
        {
            try { completion.SetResult(PickFolder(owner, currentDirectory)); }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string PickFolder(IntPtr owner, string currentDirectory)
    {
        const int apartmentThreaded = 2;
        const int cancelled = unchecked((int)0x800704C7);
        const uint inProcessServer = 1;
        const uint pickFolders = 0x20;
        const uint forceFileSystem = 0x40;
        const uint fileSystemPath = 0x80058000;
        var dialogClass = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        var dialogId = typeof(IFileDialog).GUID;
        var shellItemId = typeof(IShellItem).GUID;
        IFileDialog dialog = null;
        IShellItem initialFolder = null;
        IShellItem selectedFolder = null;

        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, apartmentThreaded));
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref dialogClass, IntPtr.Zero, inProcessServer, ref dialogId, out dialog));
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | pickFolders | forceFileSystem);
            dialog.SetTitle("选择 Spine 资源文件夹");
            dialog.SetOkButtonLabel("选择文件夹");
            if (Directory.Exists(currentDirectory) &&
                SHCreateItemFromParsingName(currentDirectory, IntPtr.Zero, ref shellItemId, out initialFolder) >= 0)
                dialog.SetFolder(initialFolder);

            var result = dialog.Show(owner);
            if (result == cancelled) return string.Empty;
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out selectedFolder);
            selectedFolder.GetDisplayName(fileSystemPath, out var path);
            try { return Marshal.PtrToStringUni(path); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        finally
        {
            if (selectedFolder != null) Marshal.ReleaseComObject(selectedFolder);
            if (initialFolder != null) Marshal.ReleaseComObject(initialFolder);
            if (dialog != null) Marshal.ReleaseComObject(dialog);
            CoUninitialize();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr window, bool accept);

    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder path, uint capacity);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr drop);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int mode);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, IntPtr outer, uint context, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IFileDialog dialog);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    // Include the unused methods to preserve the native COM vtable order.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint format, out IntPtr name);
    }
}
#endif
