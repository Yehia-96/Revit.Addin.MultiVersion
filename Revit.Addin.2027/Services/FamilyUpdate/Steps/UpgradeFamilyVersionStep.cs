namespace Revit.Addin._2027.Services.FamilyUpdate.Steps
{
    /// <summary>
    /// Brings a family file up to the Revit release that is currently running.
    /// </summary>
    /// <remarks>
    /// There is no API call that performs the upgrade: opening an older .rfa in this Revit
    /// build converts it in memory, and saving it makes the conversion permanent. The step
    /// therefore edits nothing and only tells the runner that the document has to be written.
    /// </remarks>
    public sealed class UpgradeFamilyVersionStep : IFamilyFileStep
    {
        private readonly bool _skipFilesAlreadyCurrent;

        /// <param name="skipFilesAlreadyCurrent">
        /// When true, files already saved in the running release are left alone. Turn it off to
        /// force a re-save of the whole library, which is occasionally useful to compact files
        /// that have grown over many edit cycles.
        /// </param>
        public UpgradeFamilyVersionStep(bool skipFilesAlreadyCurrent)
        {
            _skipFilesAlreadyCurrent = skipFilesAlreadyCurrent;
        }

        public string Name
        {
            get { return "Upgrade family version"; }
        }

        public string Description
        {
            get { return "Re-saves each .rfa in the Revit release that is currently running."; }
        }

        public bool ShouldProcess(FamilyFileCandidate candidate)
        {
            if (candidate == null || !candidate.IsReadable)
            {
                return false;
            }

            // A family saved in a newer Revit cannot be opened here at all, let alone downgraded.
            if (candidate.IsSavedInLaterVersion)
            {
                return false;
            }

            return !_skipFilesAlreadyCurrent || !candidate.IsSavedInCurrentVersion;
        }

        public FamilyStepOutcome Apply(FamilyFileContext context)
        {
            if (context.Candidate.IsSavedInCurrentVersion)
            {
                return FamilyStepOutcome.RequiresSave(
                    string.Format("Re-saved in format {0}.", context.CurrentVersion));
            }

            return FamilyStepOutcome.RequiresSave(string.Format(
                "Upgraded from format {0} to {1}.",
                context.Candidate.VersionLabel,
                context.CurrentVersion));
        }
    }
}
