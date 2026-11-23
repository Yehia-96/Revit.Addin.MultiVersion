using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

/// <summary>
/// The database's two sign-in procedures, in memory: one account, salts that tell nobody who exists, and the waits
/// after wrong tries. It records what it was asked.
/// </summary>
internal sealed class FakeSignInDatabase : ISignInDatabase
{
    public const string UserName = "a.wagner";
    public const string MadeUpPassword = "made up for tests";

    private static readonly int[] Waits = { 0, 0, 0, 0, 30, 60, 120 };

    private readonly Dictionary<string, int> _wrongTries = new(StringComparer.OrdinalIgnoreCase);

    public string Salt { get; set; } = Convert.ToBase64String(Enumerable.Range(7, 32).Select(i => (byte)i).ToArray());

    public string DisplayName { get; set; } = "Anna Wagner";

    public string Role { get; set; } = "Editor";

    public bool IsActive { get; set; } = true;

    /// <summary>Thrown instead of answering, when set.</summary>
    public LinkDatabaseException Failure { get; set; }

    /// <summary>Like <see cref="Failure"/>, but only when the hash is checked.</summary>
    public LinkDatabaseException CheckFailure { get; set; }

    public List<string> SaltsAskedFor { get; } = new();

    public List<(string UserName, string Hash)> Checks { get; } = new();

    public string GetSalt(string userName)
    {
        SaltsAskedFor.Add(userName);
        if (Failure != null)
            throw Failure;
        return Salt;
    }

    public SignInAnswer Check(string userName, string hash)
    {
        Checks.Add((userName, hash));
        if (CheckFailure != null)
            throw CheckFailure;

        bool isRight = IsActive && string.Equals(userName, UserName, StringComparison.OrdinalIgnoreCase)
            && hash == PasswordHash.Compute(MadeUpPassword, Salt);
        if (isRight)
        {
            _wrongTries.Remove(userName);
            return new SignInAnswer(true, DisplayName, Role, 0);
        }

        _wrongTries.TryGetValue(userName, out int before);
        _wrongTries[userName] = before + 1;
        return new SignInAnswer(false, null, null, before < Waits.Length ? Waits[before] : 300);
    }
}
