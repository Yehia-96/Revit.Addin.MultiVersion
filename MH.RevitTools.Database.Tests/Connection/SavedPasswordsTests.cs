using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class SavedPasswordsTests : IDisposable
{
    private const string FakePassword = "fakePasswordForTests1";
    private const string NewerPassword = "newerFakePassword2";

    private static readonly ConnectionSettings Office =
        ConnectionSettings.WithoutPassword("sql.example.local,1433", "MH_Database", "revit_reader", true, true);

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"), "Passwords");

    public void Dispose()
    {
        string parent = Path.GetDirectoryName(_folder);
        if (Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }

    private SavedPasswords Store(IPasswordProtection protection = null) => new(_folder, protection ?? new Reversing());

    private static ConnectionFileResult File_(ConnectionSettings settings) => ConnectionFileResult.Loaded("database.json", settings, "master.txt");

    private static void Save(SavedPasswords store, string password = FakePassword)
    {
        Assert.True(store.TrySave(Office.WithPassword(password), out string problem), problem);
        Assert.Null(problem);
    }

    private string OnlyFile() => Assert.Single(Directory.GetFiles(_folder));

    [Fact]
    public void DefaultFolder_IsThePersonsOwnAndStaysOnThePc() =>
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH", "RevitTools", "Passwords"),
            SavedPasswords.DefaultFolder);

    [Fact]
    public void NothingSaved_TheFileStillNeedsThePassword()
    {
        ConnectionFileResult file = Store().Complete(File_(Office));

        Assert.True(file.IsLoaded);
        Assert.True(file.Settings.NeedsPassword);
        Assert.False(file.IsSavedPasswordRefused);
    }

    [Fact]
    public void Saved_CompletesTheFile_InThisSessionAndInTheNext()
    {
        SavedPasswords store = Store();
        Save(store);

        foreach (SavedPasswords session in new[] { store, Store() })
        {
            ConnectionFileResult file = session.Complete(File_(Office));
            Assert.Equal(FakePassword, file.Settings.Password);
            Assert.True(file.Settings.HasEnteredPassword);
            Assert.False(file.Settings.NeedsPassword);
            Assert.Equal("database.json", file.Source);
            Assert.Equal("master.txt", file.MasterParameterFile);
        }
    }

    [Fact]
    public void TheFileOnThePc_NeverHoldsThePasswordAsItIs()
    {
        Save(Store(new WindowsPasswordProtection()));

        string content = File.ReadAllText(OnlyFile());
        Assert.DoesNotContain(FakePassword, content);
        Assert.DoesNotContain(FakePassword, Encoding.UTF8.GetString(Convert.FromBase64String(content)));
        Assert.DoesNotContain(FakePassword, Encoding.Unicode.GetString(Convert.FromBase64String(content)));
        Assert.DoesNotContain("revit_reader", OnlyFile());
    }

    [Fact]
    public void WithWindowsOwnEncryption_ItComesBack()
    {
        Save(Store(new WindowsPasswordProtection()));

        Assert.Equal(FakePassword, Store(new WindowsPasswordProtection()).Complete(File_(Office)).Settings.Password);
    }

    // A connection file changed to name another server must never get the password sent there.
    [Theory]
    [InlineData("other.example.local,1433", "MH_Database", "revit_reader")]
    [InlineData("sql.example.local,1433", "Other", "revit_reader")]
    [InlineData("sql.example.local,1433", "MH_Database", "someone")]
    public void AnotherServerDatabaseOrLogin_DoesntGetIt(string server, string database, string login)
    {
        Save(Store());

        ConnectionFileResult file = Store().Complete(File_(ConnectionSettings.WithoutPassword(server, database, login, true, true)));

        Assert.True(file.Settings.NeedsPassword);
    }

    [Fact]
    public void AFileRenamedForAnotherServer_DoesntOpen()
    {
        ConnectionSettings other = ConnectionSettings.WithoutPassword("other.example.local", "MH_Database", "revit_reader", true, true);
        SavedPasswords store = Store(new WindowsPasswordProtection());
        Save(store);
        string forOffice = OnlyFile();
        Assert.True(store.TrySave(other.WithPassword(NewerPassword), out _));
        string forOther = Assert.Single(Directory.GetFiles(_folder), path => path != forOffice);

        File.Copy(forOffice, forOther, overwrite: true);

        Assert.True(Store(new WindowsPasswordProtection()).Complete(File_(other)).Settings.NeedsPassword);
    }

    [Theory]
    [InlineData("SQL.Example.local,1433", "mh_database", "Revit_Reader")]
    [InlineData(" sql.example.local,1433 ", "MH_Database", "revit_reader")]
    public void TheSameConnectionWrittenAnotherWay_GetsIt(string server, string database, string login)
    {
        Save(Store());

        Assert.Equal(FakePassword,
            Store().Complete(File_(ConnectionSettings.WithoutPassword(server, database, login, true, true))).Settings.Password);
    }

    // Letters that upper case makes an s or a k of, so the name only looks like the server's.
    [Theory]
    [InlineData("ſql.example.local,1433", "MH_Database", "revit_reader")]
    [InlineData("sql.example.local,1433", "MH_Database", "revıt_reader")]
    [InlineData("ｓql.example.local,1433", "MH_Database", "revit_reader")]
    public void ALookAlikeName_DoesntGetIt(string server, string database, string login)
    {
        Save(Store());

        ConnectionFileResult file = Store().Complete(File_(ConnectionSettings.WithoutPassword(server, database, login, true, true)));

        Assert.True(file.Settings.NeedsPassword);
    }

    [Fact]
    public void AFileWithItsOwnPassword_IsLeftAlone()
    {
        Save(Store());
        ConnectionFileResult own = File_(new ConnectionSettings(Office.Server, Office.Database, false, Office.Login, "fromTheFile", true, true));

        Assert.Same(own, Store().Complete(own));
    }

    [Fact]
    public void WindowsSignIn_IsLeftAlone()
    {
        ConnectionFileResult dev = File_(new ConnectionSettings("(localdb)\\MSSQLLocalDB", "MH_Database", true, null, null, false, false));

        Assert.Same(dev, Store().Complete(dev));
    }

    [Fact]
    public void AFileThatCouldntBeRead_IsLeftAlone()
    {
        ConnectionFileResult failed = ConnectionFileResult.Failed("database.json", "The connection file database.json is empty.");

        Assert.Same(failed, Store().Complete(failed));
    }

    [Fact]
    public void Refused_IsNotUsedAgainInThisSession()
    {
        SavedPasswords store = Store();
        Save(store);

        store.Refuse(store.Complete(File_(Office)).Settings);

        ConnectionFileResult file = store.Complete(File_(Office));
        Assert.True(file.Settings.NeedsPassword);
        Assert.True(file.IsSavedPasswordRefused);
    }

    // It may have been the server's fault; one more try after a restart does no harm.
    [Fact]
    public void Refused_IsTriedOnceMoreAfterARestart()
    {
        SavedPasswords store = Store();
        Save(store);
        store.Refuse(store.Complete(File_(Office)).Settings);

        Assert.Equal(FakePassword, Store().Complete(File_(Office)).Settings.Password);
    }

    [Fact]
    public void AfterARefusal_ANewOneIsUsed()
    {
        SavedPasswords store = Store();
        Save(store);
        store.Refuse(store.Complete(File_(Office)).Settings);

        Save(store, NewerPassword);

        ConnectionFileResult file = store.Complete(File_(Office));
        Assert.Equal(NewerPassword, file.Settings.Password);
        Assert.False(file.IsSavedPasswordRefused);
        Assert.Equal(NewerPassword, Store().Complete(File_(Office)).Settings.Password);
        OnlyFile();
    }

    // Two Revits on one PC: the other one has the new password already.
    [Fact]
    public void AfterARefusal_OneSavedByAnotherRevitIsUsed()
    {
        SavedPasswords first = Store(), second = Store();
        Save(first);
        first.Refuse(first.Complete(File_(Office)).Settings);

        Save(second, NewerPassword);

        Assert.Equal(NewerPassword, first.Complete(File_(Office)).Settings.Password);
    }

    [Fact]
    public void ARefusalOfAnOlderOne_DoesntTakeTheNewOneAway()
    {
        SavedPasswords store = Store();
        Save(store);
        ConnectionSettings older = store.Complete(File_(Office)).Settings;
        Save(store, NewerPassword);

        store.Refuse(older);

        Assert.Equal(NewerPassword, store.Complete(File_(Office)).Settings.Password);
    }

    // The check's thread and Revit's use it at the same time.
    [Fact]
    public void SeveralThreads_GetALongWithEachOther()
    {
        SavedPasswords store = Store();
        Save(store);

        System.Threading.Tasks.Parallel.For(0, 200, i =>
        {
            ConnectionFileResult file = store.Complete(File_(Office));
            if (i % 10 == 0)
                Save(store, i % 20 == 0 ? FakePassword : NewerPassword);
            else if (i % 7 == 0 && file.Settings.HasEnteredPassword)
                store.Refuse(file.Settings);
        });

        Save(store, NewerPassword);
        Assert.Equal(NewerPassword, store.Complete(File_(Office)).Settings.Password);
        OnlyFile();
    }

    [Fact]
    public void Refusing_WhatWasNeverEntered_ChangesNothing()
    {
        SavedPasswords store = Store();
        Save(store);

        store.Refuse(new ConnectionSettings(Office.Server, Office.Database, false, Office.Login, FakePassword, true, true));
        store.Refuse(Office);

        Assert.Equal(FakePassword, store.Complete(File_(Office)).Settings.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void ADamagedFile_CountsAsNone(string content)
    {
        Save(Store(new WindowsPasswordProtection()));
        File.WriteAllText(OnlyFile(), content);

        ConnectionFileResult file = Store(new WindowsPasswordProtection()).Complete(File_(Office));

        Assert.True(file.Settings.NeedsPassword);
        Assert.False(file.IsSavedPasswordRefused);
    }

    [Fact]
    public void AHugeFile_CountsAsNone()
    {
        Save(Store());
        File.WriteAllText(OnlyFile(), new string('A', 200 * 1024));

        Assert.True(Store().Complete(File_(Office)).Settings.NeedsPassword);
    }

    [Fact]
    public void AFileThatCantBeOpened_CountsAsNone()
    {
        Save(Store());

        using (new FileStream(OnlyFile(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(Store().Complete(File_(Office)).Settings.NeedsPassword);
    }

    // The password works; only keeping it failed.
    [Fact]
    public void WhereItCantBeKept_ItIsUsedUntilRevitCloses()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_folder));
        File.WriteAllText(_folder, "a file where the folder should be");
        SavedPasswords store = Store();

        bool saved = store.TrySave(Office.WithPassword(FakePassword), out string problem);

        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.DoesNotContain(FakePassword, problem);
        Assert.Equal(FakePassword, store.Complete(File_(Office)).Settings.Password);
        Assert.True(Store().Complete(File_(Office)).Settings.NeedsPassword);
    }

    [Fact]
    public void WhereWindowsCantEncryptIt_ItIsUsedUntilRevitCloses_AndNothingIsWritten()
    {
        SavedPasswords store = Store(new Failing());

        Assert.False(store.TrySave(Office.WithPassword(FakePassword), out string problem));

        Assert.Contains("no profile", problem);
        Assert.Equal(FakePassword, store.Complete(File_(Office)).Settings.Password);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public void OnlyAnEnteredPassword_IsSaved()
    {
        SavedPasswords store = Store();

        Assert.Throws<ArgumentException>(() => store.TrySave(Office, out _));
        Assert.Throws<ArgumentException>(() =>
            store.TrySave(new ConnectionSettings("s", "d", false, "l", "fromTheFile", true, true), out _));
    }

    // Stands in for Windows' encryption: readable to nobody who doesn't know the trick, and bound to its purpose.
    private sealed class Reversing : IPasswordProtection
    {
        public byte[] Protect(byte[] secret, byte[] purpose) => secret.Reverse().Concat(purpose).ToArray();

        public byte[] Unprotect(byte[] data, byte[] purpose) =>
            data.Length >= purpose.Length && data.Skip(data.Length - purpose.Length).SequenceEqual(purpose)
                ? data.Take(data.Length - purpose.Length).Reverse().ToArray()
                : null;
    }

    private sealed class Failing : IPasswordProtection
    {
        public byte[] Protect(byte[] secret, byte[] purpose) => throw new CryptographicException("no profile");

        public byte[] Unprotect(byte[] data, byte[] purpose) => null;
    }
}
