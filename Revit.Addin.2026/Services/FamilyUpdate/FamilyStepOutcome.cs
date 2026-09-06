namespace Revit.Addin._2026.Services.FamilyUpdate
{
    /// <summary>
    /// What a single <see cref="IFamilyFileStep"/> did to one family document.
    /// </summary>
    public enum FamilyStepStatus
    {
        /// <summary>The step looked at the document and had nothing to change.</summary>
        NoChange = 0,

        /// <summary>
        /// The document must be written back to disk. Note that a step can require a save
        /// without editing anything: simply opening an older .rfa in this Revit build
        /// upgrades it in memory, and only a save makes that permanent.
        /// </summary>
        RequiresSave = 1,

        /// <summary>The step could not complete. The file is reported as failed and left alone.</summary>
        Failed = 2
    }

    /// <summary>
    /// Result of running one step against one family document.
    /// </summary>
    public sealed class FamilyStepOutcome
    {
        private FamilyStepOutcome(FamilyStepStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public FamilyStepStatus Status { get; private set; }

        public string Message { get; private set; }

        public static FamilyStepOutcome NoChange(string message = null)
        {
            return new FamilyStepOutcome(FamilyStepStatus.NoChange, message);
        }

        public static FamilyStepOutcome RequiresSave(string message)
        {
            return new FamilyStepOutcome(FamilyStepStatus.RequiresSave, message);
        }

        public static FamilyStepOutcome Failed(string message)
        {
            return new FamilyStepOutcome(FamilyStepStatus.Failed, message);
        }
    }
}
