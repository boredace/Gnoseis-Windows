/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Backup;

/// <summary>Why an encrypted backup was refused.</summary>
public enum EncryptedBackupProblem
{
    /// <summary>The file doesn't start with the encrypted backup marker.</summary>
    NotEncrypted,

    /// <summary>A format version, key derivation, cipher or reserved value this version doesn't know.</summary>
    UnknownVersion,

    /// <summary>Key derivation settings or chunk size outside the limits a reader accepts.</summary>
    ParametersOutOfRange,

    /// <summary>A chunk failed its check: the wrong passphrase, or the file was changed.</summary>
    AuthenticationFailed,

    /// <summary>The file ends early: in the header, before any chunk, or before its last chunk.</summary>
    Truncated,

    /// <summary>There is data after the last chunk.</summary>
    TrailingData,
}

/// <summary>An encrypted backup that can't be opened. Nothing has been changed when this is thrown.</summary>
public sealed class EncryptedBackupException(EncryptedBackupProblem problem)
    : Exception(MessageFor(problem))
{
    public EncryptedBackupProblem Problem { get; } = problem;

    public static string MessageFor(EncryptedBackupProblem problem) => problem switch
    {
        EncryptedBackupProblem.UnknownVersion => "Made by a newer version of Gnoseis",
        EncryptedBackupProblem.NotEncrypted => "This is not an encrypted Gnoseis backup",
        _ => "Wrong passphrase, or the file is damaged",
    };
}
