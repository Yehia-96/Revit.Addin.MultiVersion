namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// What a parameter goes on in Revit: elements (objects) or materials. Objects can still carry a
    /// material parameter's value, e.g. IDE-KAT-Material-ID, which says which material they use.
    /// </summary>
    public enum LinkTarget
    {
        Object,
        Material,
    }
}
