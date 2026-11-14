/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Backup;

/// <summary>
/// Argon2id settings that turn a backup passphrase into a master key. Every value the writer uses and
/// every limit the reader accepts is here, in one place.
/// </summary>
public sealed record BackupKdfParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    /// <summary>What a new passphrase setup always uses: 64 MiB, 3 iterations, 1 lane.</summary>
    public static BackupKdfParameters Written { get; } = new(65536, 3, 1);

    public const int MinMemoryKiB = 8 * 1024;
    public const int MaxMemoryKiB = 1024 * 1024;
    public const int MinIterations = 1;
    public const int MaxIterations = 16;
    public const int MinParallelism = 1;
    public const int MaxParallelism = 4;

    /// <summary>
    /// Whether a reader may use these settings. Checked before any work, so a crafted file can't make
    /// the app allocate gigabytes or run for hours.
    /// </summary>
    public bool IsWithinLimits =>
        MemoryKiB is >= MinMemoryKiB and <= MaxMemoryKiB
        && Iterations is >= MinIterations and <= MaxIterations
        && Parallelism is >= MinParallelism and <= MaxParallelism;
}
