-- The accounts table as the sign-in reads it, with made-up accounts; the tests add their hashes.
CREATE TABLE dbo.Users (
    UserId int NOT NULL PRIMARY KEY, Username nvarchar(100) NOT NULL, FirstName nvarchar(100) NULL, LastName nvarchar(100) NULL,
    DisplayName AS NULLIF(LTRIM(RTRIM(CONCAT(FirstName, N' ', LastName))), N''),
    PasswordHash nvarchar(max) NOT NULL, PasswordSalt nvarchar(max) NOT NULL, Role nvarchar(max) NOT NULL, IsActive bit NOT NULL);
