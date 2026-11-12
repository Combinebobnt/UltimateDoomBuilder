using System;
using System.Collections.Generic;

namespace CodeImp.DoomBuilder.Data
{
	// Narrows the names a culture StartsWith over a sprite name set can select; the predicate still decides every match
	internal sealed class SpriteNameIndex
	{
		private const int PREFIX_LENGTH = 4;
		private static readonly List<int> NO_POSITIONS = new List<int>();

		private readonly string[] names;
		private readonly Dictionary<string, List<int>> buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		private readonly List<int> slow = new List<int>();

		internal SpriteNameIndex(IEnumerable<string> allnames)
		{
			names = new List<string>(allnames).ToArray();
			for(int i = 0; i < names.Length; i++)
			{
				string name = names[i];
				if(name == null || !HasIndexablePrefix(name))
				{
					slow.Add(i);
					continue;
				}

				string key = name.Substring(0, PREFIX_LENGTH);
				List<int> bucket;
				if(!buckets.TryGetValue(key, out bucket))
				{
					bucket = new List<int>();
					buckets.Add(key, bucket);
				}
				bucket.Add(i);
			}
		}

		internal List<string> Matching(string sourcename)
		{
			List<string> result = new List<string>();
			if(!IsIndexableSource(sourcename))
			{
				foreach(string s in names)
					if(s.StartsWith(sourcename)) result.Add(s);
				return result;
			}

			List<int> bucket;
			if(!buckets.TryGetValue(sourcename, out bucket)) bucket = NO_POSITIONS;

			// Both lists are in build order, so a two-way merge restores the set's enumeration order
			int b = 0, w = 0;
			while(b < bucket.Count || w < slow.Count)
			{
				int position;
				if(w >= slow.Count || (b < bucket.Count && bucket[b] < slow[w])) position = bucket[b++];
				else position = slow[w++];

				string s = names[position];
				if(s.StartsWith(sourcename)) result.Add(s);
			}
			return result;
		}

		private static bool HasIndexablePrefix(string name)
		{
			if(name.Length < PREFIX_LENGTH) return false;
			for(int i = 0; i < PREFIX_LENGTH; i++)
				if(!IsAsciiLetterOrDigit(name[i])) return false;
			return name.Length == PREFIX_LENGTH || name[PREFIX_LENGTH] < 128;
		}

		private static bool IsIndexableSource(string sourcename)
		{
			if(sourcename == null || sourcename.Length != PREFIX_LENGTH) return false;
			foreach(char c in sourcename)
				if(!IsAsciiLetterOrDigit(c)) return false;
			return true;
		}

		private static bool IsAsciiLetterOrDigit(char c)
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
		}
	}
}
