using Autodesk.Revit.DB;
using Revit.Addin._2027.Services;
using System;
using System.Collections.Generic;

namespace Revit.Addin._2027.Helpers
{
    /// <summary>
    /// What a route decides about one value: which parameter takes it, whether it's needed there, and what
    /// Revit gets. The Excel import finds parameters by name, the database link by GUID.
    /// </summary>
    internal interface IValueRules
    {
        /// <summary>True for a parameter the route never writes.</summary>
        bool IsIgnored(string parameterName);

        /// <summary>The element's parameter for the value; null when it doesn't have it.</summary>
        Parameter Find(Element element, string parameterName, ParamMetaData definition);

        /// <summary>False when the parameter already has the value.</summary>
        bool NeedsValue(Parameter parameter, string value, ParamMetaData definition);

        /// <summary>The value in Revit's form; null when it doesn't fit the parameter, which then stays as it is.</summary>
        ParameterValue Convert(Document document, Parameter parameter, string value, ParamMetaData definition);
    }

    /// <summary>
    /// The value-writing loop the Excel import and the database link share: which values the elements still
    /// need (<see cref="Collect"/>), and writing them (<see cref="Write"/>). It never adds parameters.
    /// </summary>
    internal static class ElementValueWriter
    {
        /// <summary>The values the elements need, as pairs for <see cref="Parameter.SetMultiple"/>. Only reads.</summary>
        /// <param name="keyOf">Connects an element to its row; null for an element without one.</param>
        /// <param name="valuesByKey">The rows: per key, parameter name to value.</param>
        /// <param name="definitions">The parameters, by name; values of others are left out.</param>
        public static List<Tuple<Parameter, ParameterValue>> Collect(
            Document document,
            IEnumerable<Element> elements,
            Func<Element, string> keyOf,
            IDictionary<string, Dictionary<string, string>> valuesByKey,
            IDictionary<string, ParamMetaData> definitions,
            IValueRules rules)
        {
            var toSet = new List<Tuple<Parameter, ParameterValue>>();
            foreach (Element element in elements)
            {
                string key = keyOf(element);
                if (key == null) continue;

                Dictionary<string, string> expected;
                if (!valuesByKey.TryGetValue(key, out expected))
                    continue;

                foreach (KeyValuePair<string, string> entry in expected)
                {
                    if (rules.IsIgnored(entry.Key))
                        continue;

                    ParamMetaData definition;
                    if (!definitions.TryGetValue(entry.Key, out definition))
                        continue;

                    Parameter parameter = rules.Find(element, entry.Key, definition);
                    if (parameter == null || parameter.IsReadOnly)
                        continue;

                    if (!rules.NeedsValue(parameter, entry.Value, definition))
                        continue;

                    ParameterValue converted = rules.Convert(document, parameter, entry.Value, definition);
                    if (converted != null)
                        toSet.Add(Tuple.Create(parameter, converted));
                }
            }
            return toSet;
        }

        /// <summary>Writes the pairs in one transaction of that name.</summary>
        public static void Write(Document document, string transactionName, IList<Tuple<Parameter, ParameterValue>> values)
        {
            using (Transaction tx = new Transaction(document, transactionName))
            {
                tx.Start();
                Parameter.SetMultiple(values);
                tx.Commit();
            }
        }
    }
}
