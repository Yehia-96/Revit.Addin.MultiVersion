using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>What Add did: the parameters it bound, and what it couldn't bind and why.</summary>
    public sealed class ParameterAddResult
    {
        public ParameterAddResult(IEnumerable<Guid> added, IEnumerable<string> problems)
        {
            Added = new ReadOnlyCollection<Guid>((added ?? Enumerable.Empty<Guid>()).ToList());
            Problems = new ReadOnlyCollection<string>((problems ?? Enumerable.Empty<string>()).ToList());
        }

        /// <summary>The GUIDs of the parameters bound.</summary>
        public IReadOnlyList<Guid> Added { get; }

        /// <summary>One plain sentence per parameter or failure; empty when all went in.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Nothing was added, for this reason.</summary>
        public static ParameterAddResult Failed(string problem) => new ParameterAddResult(null, new[] { problem });

        /// <summary>The same result with one more problem, e.g. something that went wrong after adding.</summary>
        public ParameterAddResult WithProblem(string problem) =>
            new ParameterAddResult(Added, Problems.Concat(new[] { problem }));
    }
}
