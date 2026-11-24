using System.Linq;
using System.Text;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class WindowsPasswordProtectionTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("fakePasswordForTests1");
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("one purpose");

    private readonly WindowsPasswordProtection _protection = new();

    [Fact]
    public void WhatWasProtected_ComesBackForTheSamePurpose()
    {
        byte[] data = _protection.Protect(Secret, Purpose);

        Assert.Equal(Secret, _protection.Unprotect(data, Purpose));
        Assert.DoesNotContain("fakePassword", Encoding.UTF8.GetString(data));
    }

    [Fact]
    public void AnotherPurpose_GetsNothing() =>
        Assert.Null(_protection.Unprotect(_protection.Protect(Secret, Purpose), Encoding.UTF8.GetBytes("another purpose")));

    [Fact]
    public void DamagedData_GivesNothing()
    {
        byte[] data = _protection.Protect(Secret, Purpose);

        Assert.Null(_protection.Unprotect(data.Take(data.Length / 2).ToArray(), Purpose));
        Assert.Null(_protection.Unprotect(new byte[0], Purpose));
        Assert.Null(_protection.Unprotect(new byte[] { 1, 2, 3 }, Purpose));
    }
}
