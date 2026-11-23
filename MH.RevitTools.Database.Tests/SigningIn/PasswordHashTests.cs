using System;
using System.Linq;
using System.Text;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

/// <summary>
/// Pins the hash to Studio's. The value is the one Studio's own tests pin (PasswordHashServiceTests), made from a
/// made-up password with OpenSSL, so nothing here can agree with itself. If it has to change, Studio changed its
/// hashing, and both sides are released together.
/// </summary>
public sealed class PasswordHashTests
{
    private const string MadeUp = "Prüfwort für Tests 1 €";
    private const string Hash = "AVAVFbgt0zgnwaiMG2t7xNCqJnkdkMU51FPmulO4jiQ6JFdHz4WE1ld/vHjCsM0dKDAhy0n46q3e5CDEF2TWQQ==";

    private static readonly string Salt = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void TheHash_IsTheOneStudioMakes() => Assert.Equal(Hash, PasswordHash.Compute(MadeUp, Salt));

    [Fact]
    public void TheHash_Is64BytesInBase64() => Assert.Equal(88, PasswordHash.Compute("x", Salt).Length);

    [Theory]
    [InlineData(MadeUp + " ")]
    [InlineData(" " + MadeUp)]
    [InlineData("prüfwort für Tests 1 €")]
    [InlineData("Prufwort fur Tests 1 €")]
    [InlineData("")]
    public void EverythingInThePassword_Counts(string password) => Assert.NotEqual(Hash, PasswordHash.Compute(password, Salt));

    [Fact]
    public void TheSalt_Counts()
    {
        string other = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

        Assert.NotEqual(Hash, PasswordHash.Compute(MadeUp, other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")] // 16 bytes
    public void ASaltOfAnotherKind_IsRefused(string salt) => Assert.Throws<FormatException>(() => PasswordHash.Compute("x", salt));

    [Fact]
    public void WhatIsntText_IsRefused_InsteadOfHashingAReplacementForIt()
    {
        Assert.Throws<EncoderFallbackException>(() => PasswordHash.Compute("a\uD800b", Salt));
        Assert.Throws<ArgumentNullException>(() => PasswordHash.Compute(null, Salt));
    }
}
