using System;
using Autodesk.Revit.DB;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace Revit.Addin._2027.Services.FamilyUpdate
{
    /// <summary>
    /// Everything a <see cref="IFamilyFileStep"/> needs while one family document is open.
    /// </summary>
    /// <remarks>
    /// The document handed to a step is a standalone family document, not the active project,
    /// so a step is free to open its own transactions against it. The runner owns the document
    /// lifetime — a step must never close or save it.
    /// </remarks>
    public sealed class FamilyFileContext
    {
        public FamilyFileContext(
            FamilyFileCandidate candidate,
            Document familyDocument,
            RevitApp application,
            string targetPath)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            if (familyDocument == null)
            {
                throw new ArgumentNullException("familyDocument");
            }

            if (application == null)
            {
                throw new ArgumentNullException("application");
            }

            Candidate = candidate;
            FamilyDocument = familyDocument;
            Application = application;
            TargetPath = targetPath;
        }

        public FamilyFileCandidate Candidate { get; private set; }

        /// <summary>The opened family document. Owned by the runner; do not save or close it.</summary>
        public Document FamilyDocument { get; private set; }

        public RevitApp Application { get; private set; }

        /// <summary>Where the runner will write the document if any step requires a save.</summary>
        public string TargetPath { get; private set; }

        /// <summary>Revit release currently running, e.g. "2024".</summary>
        public string CurrentVersion
        {
            get { return Application.VersionNumber; }
        }
    }
}
