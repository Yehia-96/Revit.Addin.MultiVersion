using System;
using System.Collections.Generic;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Comparing
{
    /// <summary>A project as the link reads it from the MH database, in the shape that's compared with the Excel.</summary>
    public static class DatabaseLoad
    {
        public const string Source = "database";

        /// <param name="convert">Puts a value into Revit's form.</param>
        /// <param name="kindOf">What Revit makes of a data type with a unit, e.g. Length.</param>
        public static LoadedProject From(LinkProjectData data, ConvertLoaded convert, Func<string, string, string> kindOf)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (convert == null)
                throw new ArgumentNullException(nameof(convert));
            if (kindOf == null)
                throw new ArgumentNullException(nameof(kindOf));

            var project = new LoadedProjectBuilder(Source);
            var byId = new Dictionary<int, LinkParameter>();
            foreach (LinkParameter parameter in data.Parameters)
            {
                byId[parameter.ParameterId] = parameter;
                project.Parameter(new LoadedParameter(KeyOf(parameter), parameter.RevitName, kindOf(parameter.DataType, parameter.Unit),
                    parameter.Target == LinkTarget.Material));
            }

            foreach (LinkObjectValue value in data.ObjectValues)
            {
                project.Object(value.BauteilId);
                LinkParameter parameter;
                // Not the copies of what it's made of, which the link leaves out too.
                if (byId.TryGetValue(value.ParameterId, out parameter) && parameter.Target == LinkTarget.Object && !Placeholders.IsPlaceholder(value.Value))
                    project.ObjectValue(value.BauteilId, KeyOf(parameter), convert(parameter.DataType, parameter.Unit, value.Value));
            }
            foreach (LinkObjectMaterial material in data.ObjectMaterials)
                project.ObjectMaterial(material.BauteilId, material.MaterialName);
            foreach (LinkMaterial material in data.Materials)
                project.Material(material.MaterialId, material.Name, material.Group, material.SurfacePattern, material.CutPattern);
            foreach (LinkMaterialValue value in data.MaterialValues)
            {
                LinkParameter parameter;
                if (byId.TryGetValue(value.ParameterId, out parameter) && !Placeholders.IsPlaceholder(value.Value))
                    project.MaterialValue(value.MaterialId, KeyOf(parameter), convert(parameter.DataType, parameter.Unit, value.Value));
            }
            return project.Build();
        }

        private static string KeyOf(LinkParameter parameter) =>
            LoadedProjectBuilder.KeyOf(parameter.SetCode, parameter.GroupCode, parameter.Name);
    }
}
