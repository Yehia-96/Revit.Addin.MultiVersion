namespace Revit.Addin._2024.Services.FamilyUpdate
{
    /// <summary>
    /// Decides where the runner writes each processed .rfa file.
    /// </summary>
    public enum FamilyOutputMode
    {
        /// <summary>
        /// Overwrite the original .rfa. Every original is copied into a timestamped backup
        /// folder before it is touched, so the previous version stays recoverable.
        /// The family name — and therefore its identity on reload — is preserved.
        /// </summary>
        InPlaceWithBackup = 0,

        /// <summary>
        /// Leave the source directory completely untouched and write the processed families
        /// into a separate output directory, mirroring the source folder structure. The old
        /// library keeps its original Revit version, the new one holds the upgraded copies.
        /// </summary>
        CopyToSeparateFolder = 1
    }
}
