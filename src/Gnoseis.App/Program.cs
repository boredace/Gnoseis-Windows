/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Gnoseis.App;

/// <summary>Replaces the entry point the XAML compiler generates, to restrict where DLLs load from first.</summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        // Takes the current folder out of the DLL search order. SetDefaultDllDirectories would be stricter,
        // but with it WinUI can't load its own resources and the app stops at start-up, whichever flags are used.
        if (!SetDllDirectoryW(""))
        {
            throw new Win32Exception();
        }

        XamlGeneratedProgram.XamlGeneratedMain();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectoryW(string pathName);
}
