using System;

namespace Workshop
{
    // Finds a remembered recipe among the station's own recipes (spec §5.2 gate 5). Recipe IDs are not unique in
    // 0.5.19 (10421 appears twice), so a match on ID and Name wins over one on ID alone. Returns a ShallowCopy, as
    // manufacturing.CheckItems does (SRC/manufacturing.cs:592): the copy shares Input/Output and metaSettings with the
    // database recipe, and Workshop never writes to either.
    internal static class RecipeLookup
    {
        internal static Recipe Find(CraftingBase cb, int id, string name)
        {
            try
            {
                var all = Stations.Recipes(cb);
                if (all == null || id <= 0) return null;
                Recipe byId = null;
                foreach (var r in all)
                {
                    if (r == null || r.ID != id) continue;
                    if (name != null && r.Name == name) return r;
                    if (byId == null) byId = r;
                }
                return byId;
            }
            catch (Exception e) { Log.Exception("recipe lookup", e); return null; }
        }

        internal static Recipe Copy(CraftingBase cb, int id, string name)
        {
            var r = Find(cb, id, name);
            if (r == null) return null;
            if (r.Input == null) r.SetItems();
            var copy = r.ShallowCopy();
            if (copy.Input == null) copy.SetItems();
            return copy;
        }
    }
}
