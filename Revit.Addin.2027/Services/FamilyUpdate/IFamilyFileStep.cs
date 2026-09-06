namespace Revit.Addin._2027.Services.FamilyUpdate
{
    /// <summary>
    /// One unit of work applied to every family file in a batch run.
    /// </summary>
    /// <remarks>
    /// Steps are ticked on and off in the options dialog and executed in registration order by
    /// <see cref="FamilyUpdateRunner"/>. Adding a new library-wide rule — seeding shared
    /// parameters, stripping a parameter, purging unused elements — means writing one more
    /// implementation of this interface and registering it; the runner, the progress UI and
    /// the reporting stay untouched.
    /// </remarks>
    public interface IFamilyFileStep
    {
        /// <summary>Label shown in the options dialog and in the run report.</summary>
        string Name { get; }

        /// <summary>One-line explanation shown next to the checkbox in the options dialog.</summary>
        string Description { get; }

        /// <summary>
        /// Cheap pre-open filter, called with header information only. Return false when this
        /// step has nothing to do for the file. A file that no enabled step wants is never
        /// opened, which is what keeps a run over a large library from taking hours.
        /// </summary>
        bool ShouldProcess(FamilyFileCandidate candidate);

        /// <summary>
        /// Runs against the opened family document. Implementations must not save or close
        /// the document — returning <see cref="FamilyStepStatus.RequiresSave"/> tells the
        /// runner to write it out once every step has run.
        /// </summary>
        FamilyStepOutcome Apply(FamilyFileContext context);
    }
}
