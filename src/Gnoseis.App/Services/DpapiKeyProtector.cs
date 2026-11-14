/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using System.Text;
using Gnoseis.Core.Backup;

namespace Gnoseis.App.Services;

/// <summary>
/// DPAPI for the current Windows user: the stored backup key opens only for this user on this PC. A
/// portable copy moved to another PC or account can't open it and asks for the passphrase instead.
/// </summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("org.gnoseis.backup/v1/stored-key");

    public static StoredBackupKey StoredBackupKey { get; } = new(AppPaths.BackupKeyFile, new DpapiKeyProtector());

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
