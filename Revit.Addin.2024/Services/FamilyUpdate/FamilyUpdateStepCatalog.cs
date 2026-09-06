using System;
using System.Collections.Generic;
using Revit.Addin._2024.Services.FamilyUpdate.Steps;

namespace Revit.Addin._2024.Services.FamilyUpdate
{
    /// <summary>
    /// A step as offered to the user, before the run options are known.
    /// </summary>
    public sealed class FamilyUpdateStepDescriptor
    {
        private readonly Func<FamilyUpdateOptions, IFamilyFileStep> _factory;

        public FamilyUpdateStepDescriptor(
            string name,
            string description,
            bool enabledByDefault,
            Func<FamilyUpdateOptions, IFamilyFileStep> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }

            Name = name;
            Description = description;
            EnabledByDefault = enabledByDefault;
            _factory = factory;
        }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public bool EnabledByDefault { get; private set; }

        /// <summary>Builds the step once the user has confirmed the run options.</summary>
        public IFamilyFileStep Create(FamilyUpdateOptions options)
        {
            return _factory(options);
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The single registration point for the pipeline. Adding a library-wide rule means writing
    /// an <see cref="IFamilyFileStep"/> and adding one descriptor here — the options dialog, the
    /// runner and the reporting pick it up with no further changes.
    /// </summary>
    public static class FamilyUpdateStepCatalog
    {
        public static IList<FamilyUpdateStepDescriptor> CreateDefault()
        {
            return new List<FamilyUpdateStepDescriptor>
            {
                new FamilyUpdateStepDescriptor(
                    "Upgrade family version",
                    "Re-saves each .rfa in the Revit release that is currently running.",
                    true,
                    options => new UpgradeFamilyVersionStep(options.SkipFilesAlreadyCurrent))
            };
        }
    }
}
