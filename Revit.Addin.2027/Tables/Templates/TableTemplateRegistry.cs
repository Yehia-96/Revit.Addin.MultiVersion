using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.Tables.Templates
{
    /// <summary>
    /// Registry of all known fixed-format table templates. New templates are added by
    /// instantiating them in <see cref="GetAll"/> — no other wiring is required.
    /// </summary>
    public static class TableTemplateRegistry
    {
        public static IReadOnlyList<ITableTemplate> GetAll()
        {
            return new ITableTemplate[]
            {
                new EinbauteileTableTemplate(),
                // Add new templates here. Each must implement ITableTemplate.
            };
        }

        public static ITableTemplate FindByKey(string key)
        {
            return GetAll().FirstOrDefault(t => t.TemplateKey == key);
        }
    }
}
