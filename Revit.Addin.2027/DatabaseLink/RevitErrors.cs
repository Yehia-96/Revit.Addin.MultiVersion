using System;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>Which exceptions mean Revit refused an API call, so the link can say so instead of failing.</summary>
    internal static class RevitErrors
    {
        // Revit's ArgumentException and InvalidOperationException derive from the System ones, not from
        // Revit's own ApplicationException, so all three are needed.
        public static bool IsRefusal(Exception error) =>
            error is Autodesk.Revit.Exceptions.ApplicationException
            || error is ArgumentException
            || error is InvalidOperationException;
    }
}
