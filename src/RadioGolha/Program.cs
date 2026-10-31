using System.Runtime.InteropServices;

namespace RadioGolha;

internal static class Program
{
    private const string MutexName = "RadioGolha_SingleInstance_Mutex";
    private const string WindowTitle = "Radio Golha";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SwRestore = 9;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // Only one instance of the app is allowed. If another instance is
        // already running, bring its window to the foreground and exit.
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            FocusExistingInstance();
            return;
        }

        try
        {
            // Enables visual styles and the HiDPI mode configured in the .csproj
            // (PerMonitorV2 - the C# equivalent of SetProcessDpiAwareness(1)).
            ApplicationConfiguration.Initialize();
            Application.Run(new RadioApp());
        }
        finally
        {
            // Keep the mutex alive until the app exits.
        }
    }

    private static void FocusExistingInstance()
    {
        // The window may take a moment to be created; retry briefly.
        for (var i = 0; i < 20; i++)
        {
            var hWnd = FindWindow(null, WindowTitle);
            if (hWnd != IntPtr.Zero)
            {
                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SwRestore);
                SetForegroundWindow(hWnd);
                return;
            }
            Thread.Sleep(50);
        }
    }
}
