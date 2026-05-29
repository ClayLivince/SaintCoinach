using System.Collections;
using System.Collections.Generic;

using SaintCoinach.Ex;

namespace Godbert {
    public static class SheetRows {
        /// <summary>
        /// Enumerate a sheet's rows through its NON-GENERIC enumerator.
        ///
        /// This matters for Variant-2 (sub-row) sheets. A <c>XivSheet2</c> inherits
        /// <c>IEnumerable&lt;XivRow&gt;</c> from its base and only adds the sub-row enumerator
        /// via a `new` method + an explicit non-generic <c>IEnumerable.GetEnumerator</c>.
        /// LINQ's <c>Cast&lt;IRow&gt;()</c>/<c>Cast&lt;object&gt;()</c> detect the covariant
        /// <c>IEnumerable&lt;XivRow&gt;</c> and enumerate THAT — yielding **parent** rows whose
        /// <c>GetRaw</c>/indexer throw ("Use GetSubRow instead"). Iterating the non-generic
        /// <c>IEnumerable</c> instead hits the overridden enumerator and yields the real
        /// sub-rows. For Variant-1 sheets it yields the normal rows, so this is safe for both.
        /// </summary>
        public static IEnumerable<IRow> AsRows(IEnumerable sheet) {
            if (sheet == null)
                yield break;
            foreach (var o in sheet)
                if (o is IRow row)
                    yield return row;
        }
    }
}
