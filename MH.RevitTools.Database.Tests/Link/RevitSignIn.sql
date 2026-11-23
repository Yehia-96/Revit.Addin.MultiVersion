-- The tests' copy of Studio's migration 20261123154250_AddRevitSignIn (its UpBatches), so the add-in's side of the
-- sign-in can be tried on a throwaway database. The migration is the one source; keep this in line with it. Never run
-- it on MH_Database.
CREATE TABLE dbo.RevitSignInSecret
(
    Id     tinyint    NOT NULL CONSTRAINT PK_RevitSignInSecret PRIMARY KEY
                               CONSTRAINT CK_RevitSignInSecret_OneRow CHECK (Id = 1),
    Secret binary(32) NOT NULL
);
INSERT dbo.RevitSignInSecret (Id, Secret) VALUES (1, CRYPT_GEN_RANDOM(32));
GO
CREATE TABLE dbo.RevitSignInWaits
(
    NameKey        binary(32)   NOT NULL CONSTRAINT PK_RevitSignInWaits PRIMARY KEY CLUSTERED,
    WrongTries     int          NOT NULL,
    LastWrongAtUtc datetime2(3) NOT NULL,
    WaitUntilUtc   datetime2(3) NULL
);
GO
CREATE SEQUENCE dbo.RevitSignInLogIds AS bigint START WITH 1 INCREMENT BY 1;
CREATE TABLE dbo.RevitSignInLog
(
    Id       bigint        NOT NULL CONSTRAINT PK_RevitSignInLog PRIMARY KEY CLUSTERED,
    AtUtc    datetime2(3)  NOT NULL,
    UserId   int           NULL,
    Result   varchar(10)   NOT NULL CONSTRAINT CK_RevitSignInLog_Result
                                    CHECK (Result IN ('Ok', 'Wrong', 'Unknown', 'Inactive', 'Waiting')),
    HostName nvarchar(128) NULL
);
GO
CREATE OR ALTER FUNCTION dbo.RevitSignInFold (@Username nvarchar(256))
RETURNS nvarchar(256)
WITH SCHEMABINDING
AS
BEGIN
    RETURN UPPER((CASE WHEN DATALENGTH(@Username) > 200 THEN @Username
                       ELSE RTRIM(ISNULL(@Username, N'')) END) COLLATE Latin1_General_100_CI_AS);
END;
GO
CREATE OR ALTER FUNCTION dbo.RevitSignInHmac (@Purpose binary(1), @Fold nvarchar(256))
RETURNS binary(32)
AS
BEGIN
    DECLARE @Secret binary(32) = (SELECT s.Secret FROM dbo.RevitSignInSecret AS s WHERE s.Id = 1);
    IF @Secret IS NULL RETURN NULL;

    DECLARE @Inner varbinary(128) = 0x, @Outer varbinary(128) = 0x, @At int = 1, @Part bigint;
    WHILE @At < 128
    BEGIN
        SET @Part = CAST(CAST(SUBSTRING(@Secret, @At, 8) AS binary(8)) AS bigint);
        SET @Inner += CAST(@Part ^ CAST(0x3636363636363636 AS bigint) AS binary(8));
        SET @Outer += CAST(@Part ^ CAST(0x5C5C5C5C5C5C5C5C AS bigint) AS binary(8));
        SET @At += 8;
    END;

    RETURN CAST(SUBSTRING(
        HASHBYTES('SHA2_512', @Outer
            + HASHBYTES('SHA2_512', @Inner + @Purpose + CAST(@Fold AS varbinary(512)))), 1, 32) AS binary(32));
END;
GO
CREATE OR ALTER FUNCTION dbo.RevitSignInAccount (@Fold nvarchar(256))
RETURNS TABLE
AS
RETURN
    SELECT u.UserId,
           CAST(CASE WHEN u.IsActive = 1 AND DATALENGTH(u.PasswordSalt) = 88 AND DATALENGTH(u.PasswordHash) = 176
                     THEN 1 ELSE 0 END AS bit) AS MaySignIn,
           u.PasswordSalt,
           u.PasswordHash,
           COALESCE(u.DisplayName, u.Username) AS DisplayName,
           u.Role
    FROM dbo.Users AS u WITH (INDEX (0))
    WHERE DATALENGTH(@Fold) > 0
      AND dbo.RevitSignInFold(u.Username) COLLATE Latin1_General_100_BIN2 = @Fold COLLATE Latin1_General_100_BIN2
      AND DATALENGTH(dbo.RevitSignInFold(u.Username)) = DATALENGTH(@Fold);
GO
CREATE OR ALTER PROCEDURE dbo.RevitSignInWriteLog
    @Now    datetime2(3),
    @UserId int,
    @Result varchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id bigint = NEXT VALUE FOR dbo.RevitSignInLogIds;
    INSERT dbo.RevitSignInLog (Id, AtUtc, UserId, Result, HostName)
    VALUES (@Id, @Now, @UserId, @Result, HOST_NAME());

    -- 90 days, and a million rows at most: anyone with the connection file can cause rows. Each
    -- try removes up to ten, and leaves those another one is removing.
    DELETE l
    FROM (SELECT TOP (10) l.Id, l.AtUtc
          FROM dbo.RevitSignInLog AS l WITH (READCOMMITTEDLOCK, READPAST, ROWLOCK)
          ORDER BY l.Id) AS l
    WHERE l.AtUtc < DATEADD(day, -90, @Now) OR l.Id <= @Id - 1000000;
END;
GO
CREATE OR ALTER PROCEDURE revit.GetSignInSalt
    @Username nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;

    -- In a SERIALIZABLE transaction of the caller's, reading Users would keep all of it locked
    -- until the caller is done, and nobody could sign in to Studio.
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    SET ROWCOUNT 0;
    SET TEXTSIZE 2147483647;
    SET STATISTICS IO OFF;
    SET STATISTICS TIME OFF;

    DECLARE @Fold nvarchar(256) = dbo.RevitSignInFold(@Username);
    DECLARE @MadeUp binary(32) = dbo.RevitSignInHmac(0x01, @Fold);
    IF @MadeUp IS NULL
    BEGIN;
        THROW 51001, N'dbo.RevitSignInSecret is empty, so nobody can sign in.', 1;
    END;

    DECLARE @Salt nvarchar(256) = (SELECT @MadeUp AS [*] FOR XML PATH(''));      -- base64
    SELECT @Salt = a.PasswordSalt FROM dbo.RevitSignInAccount(@Fold) AS a WHERE a.MaySignIn = 1;

    SELECT @Salt AS Salt;
END;
GO
CREATE OR ALTER PROCEDURE revit.CheckSignIn
    @Username nvarchar(256),
    @Hash     nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;

    -- Nobody may roll the count back. Inside a transaction of the caller's (INSERT … EXEC is one
    -- too), the caller could, after each guess; so it doesn't run there. IMPLICIT_TRANSACTIONS
    -- would open one around the count. And the answer comes only after the COMMIT below.
    IF @@TRANCOUNT <> 0
    BEGIN;
        THROW 51000, N'revit.CheckSignIn does not run inside a transaction.', 1;
    END;
    SET IMPLICIT_TRANSACTIONS OFF;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    -- The caller's other settings. Statistics: their messages, sent while the name is locked,
    -- could keep it locked for as long as the caller doesn't read them.
    SET ROWCOUNT 0;
    SET TEXTSIZE 2147483647;
    SET STATISTICS IO OFF;
    SET STATISTICS TIME OFF;

    DECLARE @Fold nvarchar(256) = dbo.RevitSignInFold(@Username);
    DECLARE @Key binary(32) = dbo.RevitSignInHmac(0x02, @Fold);
    IF @Key IS NULL
    BEGIN;
        THROW 51001, N'dbo.RevitSignInSecret is empty, so nobody can sign in.', 1;
    END;

    DECLARE @WrongTries int = 0, @LastWrongAtUtc datetime2(3), @WaitUntilUtc datetime2(3), @Now datetime2(3);
    DECLARE @UserId int, @MaySignIn bit = 0, @StoredHash nvarchar(256), @DisplayName nvarchar(201), @Role nvarchar(50);
    DECLARE @Result varchar(10);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- One try per name at a time, checked and counted as one step: whoever gets this lock
        -- first is done before the next one looks. HOLDLOCK holds the place of a name not there yet.
        SELECT @WrongTries = w.WrongTries, @LastWrongAtUtc = w.LastWrongAtUtc, @WaitUntilUtc = w.WaitUntilUtc
        FROM dbo.RevitSignInWaits AS w WITH (UPDLOCK, HOLDLOCK)
        WHERE w.NameKey = @Key;

        SET @Now = SYSUTCDATETIME();      -- only now, after waiting for the lock

        SELECT @UserId = a.UserId, @MaySignIn = a.MaySignIn, @StoredHash = a.PasswordHash,
               @DisplayName = a.DisplayName, @Role = a.Role
        FROM dbo.RevitSignInAccount(@Fold) AS a;

        IF @LastWrongAtUtc < DATEADD(day, -1, @Now)      -- a day without a wrong try
            SELECT @WrongTries = 0, @WaitUntilUtc = NULL;

        -- A wait that ends more than 5 minutes from now was set by a clock since put back.
        IF @WaitUntilUtc <= @Now OR @WaitUntilUtc > DATEADD(second, 300, @Now)
            SET @WaitUntilUtc = NULL;

        IF @WaitUntilUtc IS NOT NULL
            SET @Result = 'Waiting';      -- unchecked, and not counted

        -- Compared as bytes: base64 tells upper from lower case, the database's = doesn't.
        ELSE IF @MaySignIn = 1
            AND DATALENGTH(@Hash) = 176
            AND CAST(@Hash AS varbinary(512)) = CAST(@StoredHash AS varbinary(512))
        BEGIN
            SET @Result = 'Ok';
            DELETE FROM dbo.RevitSignInWaits WHERE NameKey = @Key;
        END

        ELSE
        BEGIN
            -- The same for an unknown name, an inactive account and a wrong password.
            SET @Result = CASE WHEN @UserId IS NULL THEN 'Unknown' WHEN @MaySignIn = 0 THEN 'Inactive' ELSE 'Wrong' END;
            SET @WrongTries = CASE WHEN @WrongTries < 1000 THEN @WrongTries + 1 ELSE @WrongTries END;
            SET @WaitUntilUtc = CASE WHEN @WrongTries >= 5
                THEN DATEADD(second, CASE @WrongTries WHEN 5 THEN 30 WHEN 6 THEN 60 WHEN 7 THEN 120 ELSE 300 END, @Now) END;

            UPDATE dbo.RevitSignInWaits
            SET WrongTries = @WrongTries, LastWrongAtUtc = @Now, WaitUntilUtc = @WaitUntilUtc
            WHERE NameKey = @Key;
            IF @@ROWCOUNT = 0
                INSERT dbo.RevitSignInWaits (NameKey, WrongTries, LastWrongAtUtc, WaitUntilUtc)
                VALUES (@Key, @WrongTries, @Now, @WaitUntilUtc);

            -- Anyone with the connection file can make up names. Of those whose key shares its
            -- first two bytes, the 16 tried last stay (about a million in all), and none for more
            -- than a day after its last wrong try. This name's own count always stays.
            DELETE w
            FROM (SELECT w.LastWrongAtUtc, ROW_NUMBER() OVER (ORDER BY w.LastWrongAtUtc DESC) AS Place
                  FROM dbo.RevitSignInWaits AS w WITH (READCOMMITTEDLOCK, READPAST, ROWLOCK)
                  WHERE w.NameKey >= CAST(SUBSTRING(@Key, 1, 2) AS binary(32))
                    AND w.NameKey <= SUBSTRING(@Key, 1, 2) + 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF
                    AND w.NameKey <> @Key) AS w
            WHERE w.Place > 15 OR w.LastWrongAtUtc < DATEADD(day, -1, @Now);
        END;

        EXEC dbo.RevitSignInWriteLog @Now, @UserId, @Result;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- Here and not in the caller's CATCH, which could keep the name locked.
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT CAST(CASE WHEN @Result = 'Ok' THEN 1 ELSE 0 END AS bit) AS IsValid,
           CASE WHEN @Result = 'Ok' THEN @DisplayName END AS DisplayName,
           CASE WHEN @Result = 'Ok' THEN @Role END AS Role,
           CAST(CASE WHEN @WaitUntilUtc IS NULL THEN 0
                     ELSE CEILING(DATEDIFF(millisecond, @Now, @WaitUntilUtc) / 1000.0) END AS int) AS WaitSeconds;
END;
