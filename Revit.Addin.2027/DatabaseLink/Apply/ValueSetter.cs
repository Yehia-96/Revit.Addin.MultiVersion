using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>One value on its way into one element or material.</summary>
    internal sealed class PendingValue
    {
        public PendingValue(Tuple<Parameter, ParameterValue> pair, string key, bool isMaterial)
        {
            Pair = pair;
            Key = key;
            IsMaterial = isMaterial;
            ElementId = pair.Item1.Element.Id;
            ParameterGuid = pair.Item1.GUID;
            ParameterName = pair.Item1.Definition.Name;
            Id = IdOf(pair.Item1);
        }

        public Tuple<Parameter, ParameterValue> Pair { get; }

        /// <summary>The Bauteil_ID or the Material-ID.</summary>
        public string Key { get; }

        public bool IsMaterial { get; }

        public ElementId ElementId { get; }

        public Guid ParameterGuid { get; }

        public string ParameterName { get; }

        /// <summary>Tells one element's parameter from all others.</summary>
        public string Id { get; }

        public static string IdOf(Parameter parameter) => parameter.Element.Id.Value + "|" + parameter.Id.Value;
    }

    /// <summary>
    /// Writes values into their parameters, for Apply and for Create. Inside a transaction; what Revit refuses is
    /// told to the tally and doesn't stop the others.
    /// </summary>
    internal static class ValueSetter
    {
        // All at once, which is quick; when Revit throws for one of them, one by one, so that one doesn't stop
        // the others.
        /// <returns>The values that went in.</returns>
        public static List<PendingValue> SetAll(Document document, List<PendingValue> pending, ApplyTally tally)
        {
            if (pending.Count == 0)
                return pending;
            using (var attempt = new SubTransaction(document))
            {
                attempt.Start();
                try
                {
                    List<PendingValue> set = Set(pending, tally);
                    attempt.Commit();
                    return set;
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    attempt.RollBack();
                }
            }

            var one = new List<PendingValue>();
            foreach (PendingValue value in pending)
            {
                try
                {
                    one.AddRange(Set(new List<PendingValue> { value }, tally));
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    tally.Refused(value.ParameterName, ex.Message);
                }
            }
            return one;
        }

        private static List<PendingValue> Set(List<PendingValue> values, ApplyTally tally)
        {
            var refused = new HashSet<string>(StringComparer.Ordinal);
            IList<Parameter> notSet = Parameter.SetMultiple(values.Select(value => value.Pair).ToList());
            foreach (Parameter parameter in notSet ?? new Parameter[0])
                refused.Add(PendingValue.IdOf(parameter));

            foreach (PendingValue value in values.Where(value => refused.Contains(value.Id)))
                tally.Refused(value.ParameterName, "Revit didn't take the value");
            return values.Where(value => !refused.Contains(value.Id)).ToList();
        }
    }
}
