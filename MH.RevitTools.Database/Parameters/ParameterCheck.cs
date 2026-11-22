using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>Which rows a Check parameters tab shows.</summary>
    public enum ParameterFilter
    {
        All,

        /// <summary>Missing ones, those Add can bind and those it can't.</summary>
        Missing,

        OtherGuid,

        /// <summary>Ready ones, and those added just now.</summary>
        Ready,
    }

    /// <summary>
    /// Check parameters, without any UI: every database parameter on each of its categories, set
    /// against the model's project parameters, and what Add would bind from the master file. A
    /// parameter is found by its GUID, never by its name; a name only matters when it clashes.
    /// </summary>
    public sealed class ParameterCheck
    {
        private ParameterCheck(
            MasterParameterFile masterFile, string fileProblem, List<ParameterCheckRow> rows,
            List<ParameterToAdd> toAdd, List<ParameterToReplace> toReplace)
        {
            MasterFile = masterFile;
            FileProblem = fileProblem;
            Rows = rows.AsReadOnly();
            ToAdd = toAdd.AsReadOnly();
            ToReplace = toReplace.AsReadOnly();
        }

        public MasterParameterFile MasterFile { get; }

        /// <summary>Why nothing can be added from the master file; null when it can.</summary>
        public string FileProblem { get; }

        /// <summary>Problems first: other GUIDs, then missing ones, then ready ones.</summary>
        public IReadOnlyList<ParameterCheckRow> Rows { get; }

        /// <summary>What Add binds, one entry per parameter.</summary>
        public IReadOnlyList<ParameterToAdd> ToAdd { get; }

        /// <summary>What Replace swaps: the Other GUID parameters whose database parameter can be added.</summary>
        public IReadOnlyList<ParameterToReplace> ToReplace { get; }

        public int ReadyCount => Rows.Count(r => r.IsReady);

        /// <param name="parameters">The database's parameters that go to Revit.</param>
        /// <param name="masterFile">The master file as read; null or unread when it couldn't be.</param>
        /// <param name="model">The model's project parameters.</param>
        /// <param name="justAdded">GUIDs that Add bound just now, so their rows say Added.</param>
        public static ParameterCheck Run(
            IEnumerable<LinkParameter> parameters,
            MasterParameterFile masterFile,
            IEnumerable<ModelBinding> model,
            IEnumerable<Guid> justAdded = null)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            List<LinkParameter> dictionary = parameters.ToList();
            var context = new Context(model.ToList(), masterFile, FileProblemFor(masterFile, dictionary), justAdded);
            var rows = new List<ParameterCheckRow>();
            var toAdd = new List<ParameterToAdd>();
            var toReplace = new List<ParameterToReplace>();
            foreach (LinkParameter parameter in dictionary)
            {
                ModelBinding sameGuid = context.BindingWithGuid(parameter.Guid);
                List<ParameterCheckRow> parameterRows = CategoriesOf(parameter)
                    .Select(category => RowFor(parameter, category, sameGuid, context))
                    .ToList();
                rows.AddRange(parameterRows);

                List<string> missing = parameterRows.Where(r => r.State == ParameterState.Missing).Select(r => r.Category).ToList();
                if (missing.Count > 0)
                    toAdd.Add(new ParameterToAdd(parameter, missing, sameGuid != null));

                // One swap per model parameter in the way: it may block the parameter on several categories.
                foreach (IGrouping<ModelBinding, ParameterCheckRow> clash in parameterRows.Where(r => r.CanBeReplaced).GroupBy(r => r.Clash))
                    toReplace.Add(new ParameterToReplace(clash.Key, new ParameterToAdd(parameter, clash.Select(r => r.Category), sameGuid != null)));
            }
            return new ParameterCheck(masterFile, context.FileProblem, Order(rows), toAdd, toReplace);
        }

        public IReadOnlyList<ParameterCheckRow> Filter(ParameterFilter filter) =>
            Rows.Where(r => Fits(r, filter)).ToList().AsReadOnly();

        public int Count(ParameterFilter filter) => Rows.Count(r => Fits(r, filter));

        /// <summary>The dictionary's full name, e.g. BAU-GEO-Dicke; the plain name without a set and group.</summary>
        public static string KeyOf(LinkParameter parameter)
        {
            if (parameter == null)
                return "";
            string set = parameter.SetCode.Trim();
            string group = parameter.GroupCode.Trim();
            return set.Length == 0 || group.Length == 0 ? parameter.Name : set + "-" + group + "-" + parameter.Name;
        }

        private static ParameterCheckRow RowFor(LinkParameter parameter, string category, ModelBinding sameGuid, Context context)
        {
            if (category.Length == 0)
                return Row(parameter, category, ParameterState.CantAdd, parameter.RevitName,
                    "The database gives it no Revit category this add-in knows.");
            if (sameGuid != null && sameGuid.IsOn(category))
                return ReadyRow(parameter, category, sameGuid, context);

            ModelBinding clash = context.Bindings.FirstOrDefault(
                b => b.IsOn(category) && SameName(b.Name, parameter.RevitName) && b.Guid != parameter.Guid);
            string cantAdd = WhyItCantBeAdded(parameter, sameGuid, context);
            if (clash != null)
                return OtherGuidRow(parameter, category, clash, cantAdd);

            return cantAdd != null
                ? Row(parameter, category, ParameterState.CantAdd, parameter.RevitName, cantAdd)
                : Row(parameter, category, ParameterState.Missing, parameter.RevitName, "");
        }

        // Replace is only offered when the database's parameter can take the old one's place.
        private static ParameterCheckRow OtherGuidRow(LinkParameter parameter, string category, ModelBinding clash, string cantAdd)
        {
            string note = (clash.Guid.HasValue ? "Same name, other GUID" : "A project parameter of this name that isn't shared")
                + (cantAdd == null
                    ? ": left empty unless you replace it."
                    : ": left empty. It can't be replaced: " + char.ToLowerInvariant(cantAdd[0]) + cantAdd.Substring(1));
            return new ParameterCheckRow(parameter, category, ParameterState.OtherGuid, clash.Name, note, clash, cantAdd == null);
        }

        private static ParameterCheckRow ReadyRow(LinkParameter parameter, string category, ModelBinding binding, Context context)
        {
            bool added = context.JustAdded.Contains(parameter.Guid);
            string note = added ? "Added from the master file."
                : SameName(binding.Name, parameter.RevitName) ? ""
                : "The database calls it “" + parameter.RevitName + "” now.";
            return Row(parameter, category, added ? ParameterState.Added : ParameterState.Ready, binding.Name, note);
        }

        private static string WhyItCantBeAdded(LinkParameter parameter, ModelBinding sameGuid, Context context)
        {
            if (sameGuid != null && !sameGuid.IsInstance)
                return "A type parameter in this model: add the category by hand.";
            if (parameter.Guid == Guid.Empty)
                return "The database gives it no GUID.";
            if (context.FileProblem != null)
                return "Needs the master shared parameter file.";
            if (!context.MasterFile.Holds(parameter.Guid))
                return "Not in " + context.MasterFile.FileName + " yet: Studio writes it a few seconds after a change.";
            return null;
        }

        private static string FileProblemFor(MasterParameterFile masterFile, List<LinkParameter> dictionary)
        {
            if (masterFile == null)
                return "The master shared parameter file wasn't read.";
            if (!masterFile.IsRead)
                return masterFile.Problem;

            List<Guid> guids = dictionary.Where(p => p.Guid != Guid.Empty).Select(p => p.Guid).ToList();
            if (guids.Count > 0 && !guids.Any(masterFile.Holds))
                return masterFile.FileName + " holds none of this database's parameters, so it belongs to another "
                    + "database. Check \"masterParameterFile\" in the connection file.";
            return null;
        }

        // A parameter without a category still gets a row, so the dialog can say why it can't be added.
        private static IEnumerable<string> CategoriesOf(LinkParameter parameter) =>
            parameter.Categories.Count > 0 ? (IEnumerable<string>)parameter.Categories : new[] { "" };

        private static List<ParameterCheckRow> Order(List<ParameterCheckRow> rows) =>
            rows.OrderBy(r => Priority(r.State))
                .ThenBy(r => r.Category, StringComparer.Ordinal)
                .ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static int Priority(ParameterState state)
        {
            switch (state)
            {
                case ParameterState.OtherGuid: return 0;
                case ParameterState.Missing: return 1;
                case ParameterState.CantAdd: return 2;
                case ParameterState.Added: return 3;
                default: return 4;
            }
        }

        private static bool Fits(ParameterCheckRow row, ParameterFilter filter)
        {
            switch (filter)
            {
                case ParameterFilter.Missing: return row.State == ParameterState.Missing || row.State == ParameterState.CantAdd;
                case ParameterFilter.OtherGuid: return row.State == ParameterState.OtherGuid;
                case ParameterFilter.Ready: return row.IsReady;
                default: return true;
            }
        }

        private static ParameterCheckRow Row(LinkParameter parameter, string category, ParameterState state, string name, string note) =>
            new ParameterCheckRow(parameter, category, state, name, note);

        private static bool SameName(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        // What every row is checked against.
        private sealed class Context
        {
            public Context(List<ModelBinding> bindings, MasterParameterFile masterFile, string fileProblem, IEnumerable<Guid> justAdded)
            {
                Bindings = bindings;
                MasterFile = masterFile;
                FileProblem = fileProblem;
                JustAdded = new HashSet<Guid>(justAdded ?? Enumerable.Empty<Guid>());
            }

            public List<ModelBinding> Bindings { get; }

            public MasterParameterFile MasterFile { get; }

            public string FileProblem { get; }

            public HashSet<Guid> JustAdded { get; }

            public ModelBinding BindingWithGuid(Guid guid) =>
                guid == Guid.Empty ? null : Bindings.FirstOrDefault(b => b.Guid == guid);
        }
    }
}
