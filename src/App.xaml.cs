using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BlockLauncher;

public partial class App : Application
{
    private static readonly string ErrorLog = Path.Combine(
        Path.GetTempPath(), "hearth-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MigrateOldConfig();

        // 任何没接住的异常都写日志并提示，别让窗口直接消失
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            WriteLog(args.ExceptionObject as Exception);
        };
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        try
        {
            DarkDialog.Toast(MainWindow, "出错了：" + e.Exception.Message);
        }
        catch { }
        e.Handled = true;
    }

    /// <summary>旧名字（BlockLauncher）的配置搬过来，别让设置丢了</summary>
    private static void MigrateOldConfig()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var oldDir = Path.Combine(appData, "BlockLauncher");
            var newDir = Path.Combine(appData, "Hearth");

            if (!Directory.Exists(oldDir) || Directory.Exists(newDir)) return;

            Directory.CreateDirectory(newDir);
            foreach (var file in Directory.GetFiles(oldDir))
                File.Copy(file, Path.Combine(newDir, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(oldDir))
                CopyDirectory(dir, Path.Combine(newDir, Path.GetFileName(dir)));
        }
        catch { }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static void WriteLog(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            File.AppendAllText(ErrorLog,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                ex + Environment.NewLine + "-----" + Environment.NewLine);
        }
        catch { }
    }
}
