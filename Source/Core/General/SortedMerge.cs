using System;
using System.Collections.Generic;

namespace CodeImp.DoomBuilder
{
	public static class SortedMerge
	{
		// listed is already in key order; batch is sorted in place; oldtonew maps each listed row to its merged index.
		public static List<T> Merge<T>(IList<T> listed, List<T> batch, Func<T, string> key, out int[] oldtonew)
		{
			batch.Sort((a, b) => string.Compare(key(a), key(b), StringComparison.CurrentCulture));
			List<T> merged = new List<T>(listed.Count + batch.Count);
			oldtonew = new int[listed.Count];
			int oldindex = 0;
			int newindex = 0;
			while(oldindex < listed.Count || newindex < batch.Count)
			{
				if(oldindex < listed.Count && (newindex == batch.Count
					|| string.Compare(key(listed[oldindex]), key(batch[newindex]), StringComparison.CurrentCulture) <= 0))
				{
					oldtonew[oldindex] = merged.Count;
					merged.Add(listed[oldindex++]);
				}
				else
				{
					merged.Add(batch[newindex++]);
				}
			}
			return merged;
		}
	}
}
