/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;

namespace Gnoseis.App;

public partial class App : Application
{
    private static AppContainer? _container;

    static App()
    {
        // Last-resort record of fatal errors (e.g. during start-up, before any window can show them).
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject);
    }

    public static void WriteCrashLog(object error)
    {
        try
        {
            // With a bad argument nothing is written anywhere; before the data folder is known, in %LOCALAPPDATA%\Gnoseis.
            if (AppPaths.Argument.IsError)
            {
                return;
            }
            var folder = AppPaths.IsResolved ? AppPaths.DataFolder : AppPaths.LocalFolder;
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[{DateTime.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nothing else can be done at this point.
        }
    }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Database and repositories. Set once the database has been opened.</summary>
    public static AppContainer Container
    {
        get => _container ?? throw new InvalidOperationException("The database is not open yet.");
        set => _container = value;
    }

    /// <summary>Whether backups are encrypted, and the stored key (DPAPI). Needs the database to be open.</summary>
    public static Core.Backup.BackupEncryption BackupEncryption =>
        _backupEncryption ??= new(Container.Backup, DpapiKeyProtector.StoredBackupKey);

    private static Core.Backup.BackupEncryption? _backupEncryption;

    /// <summary>The settings of the database in use; defaults until the data folder is known.</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>Reads settings.json once the data folder is known.</summary>
    public static void LoadSettings() => Settings = AppSettings.Load(AppPaths.SettingsFile);

    public static MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // settings.json is per data folder, so it's read once start-up knows the folder (MainWindow.StartAsync).
        MainWindow = new MainWindow();
        MainWindow.Activate();
        _ = MainWindow.StartAsync();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the app running and tell the user, instead of closing with unsaved work.
        e.Handled = true;
        WriteCrashLog(e.Exception);
        _ = Dialogs.ShowErrorAsync("Something went wrong", e.Exception);
    }
}
