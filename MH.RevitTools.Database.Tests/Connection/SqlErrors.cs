using System;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Tests.Connection;

/// <summary>Makes SqlClient's own exceptions, which it lets nobody else make. By reflection, for SqlClient 5.2.2.</summary>
internal static class SqlErrors
{
    private const BindingFlags Internals = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

    /// <summary>As SqlClient reports an answer that holds these errors, in this order.</summary>
    public static SqlException Answer(params (int Number, byte Class, string Message)[] errors)
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true);
        MethodInfo add = Found(typeof(SqlErrorCollection).GetMethod("Add", Internals));
        ConstructorInfo make = Found(typeof(SqlError).GetConstructor(Internals, null, new[]
        {
            typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception),
        }, null));
        foreach ((int number, byte errorClass, string message) in errors)
            add.Invoke(collection, new[] { make.Invoke(new object[] { number, (byte)1, errorClass, "server", message, "", 0, null }) });

        MethodInfo create = Found(typeof(SqlException).GetMethod("CreateException", Internals, null,
            new[] { typeof(SqlErrorCollection), typeof(string) }, null));
        return (SqlException)create.Invoke(null, new object[] { collection, "15.0" });
    }

    private static T Found<T>(T member) where T : MemberInfo =>
        member ?? throw new InvalidOperationException("SqlClient makes its exceptions differently now; change this helper.");
}
