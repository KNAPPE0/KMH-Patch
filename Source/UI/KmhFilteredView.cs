using System;
using System.Collections.Generic;

namespace KMHPatch.UI
{
    // Keyed on the source REFERENCE: caches swap their snapshot wholesale, so a new reference means new counts.
    internal sealed class KmhFilteredView<T>
    {
        private List<T>  _rows;
        private object   _source;
        private string   _filter;
        private string   _key;

        public List<T> Get(object source, string filter, Func<List<T>> build, string key = null)
        {
            if (_rows == null || !ReferenceEquals(_source, source) || _filter != filter || _key != key)
            {
                _rows   = build() ?? new List<T>();
                _source = source;
                _filter = filter;
                _key    = key;
            }
            return _rows;
        }

        // Force a rebuild on the next draw (e.g. right after an action consumes stock).
        public void Invalidate() => _rows = null;
    }
}
