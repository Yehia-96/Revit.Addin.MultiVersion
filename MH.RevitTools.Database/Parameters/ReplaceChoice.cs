namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// The answer to Replace's further questions: about parameters that schedules or view filters use
    /// (<see cref="ReplaceUses"/>), and about parameters Revit deletes other elements with
    /// (<see cref="ReplaceDeletions"/>).
    /// </summary>
    public enum ReplaceChoice
    {
        /// <summary>Replace nothing.</summary>
        Cancel,

        /// <summary>Replace them all: those in use too, or deleting the listed elements with them.</summary>
        ReplaceAll,

        /// <summary>Leave the parameters the question is about as they are; replace the others.</summary>
        ReplaceOthers,
    }
}
