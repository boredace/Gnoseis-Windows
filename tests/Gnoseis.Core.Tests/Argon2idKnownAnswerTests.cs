/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Gnoseis.Core.Tests;

/// <summary>Bouncy Castle's Argon2id against the test vector in RFC 9106, section 5.3.</summary>
public class Argon2idKnownAnswerTests
{
    [Fact]
    public void Argon2id_MatchesRfc9106TestVector()
    {
        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithMemoryAsKB(32)
            .WithIterations(3)
            .WithParallelism(4)
            .WithSalt(Filled(16, 0x02))
            .WithSecret(Filled(8, 0x03))
            .WithAdditional(Filled(12, 0x04))
            .Build();
        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);

        var tag = new byte[32];
        generator.GenerateBytes(Filled(32, 0x01), tag);

        Assert.Equal(
            "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659",
            Convert.ToHexStringLower(tag));
    }

    private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();
}
