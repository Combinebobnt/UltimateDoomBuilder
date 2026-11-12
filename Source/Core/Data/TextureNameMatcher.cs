using System;
using System.Text.RegularExpressions;

namespace CodeImp.DoomBuilder.Data
{
	internal enum TextureNameMatchMode { Name, Fuzzy, Regex }

	internal sealed class TextureNameMatcher
	{
		private readonly TextureNameMatchMode mode;
		private readonly string query;
		private readonly string[] tokens;
		private readonly Regex regex;

		public string Error { get; private set; }
		public bool TimedOut { get; private set; }

		public TextureNameMatcher(TextureNameMatchMode mode, string query)
		{
			if(!Enum.IsDefined(typeof(TextureNameMatchMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
			this.mode = mode;
			this.query = (query ?? string.Empty).ToUpperInvariant();
			if(mode == TextureNameMatchMode.Fuzzy)
				tokens = this.query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			else if(mode == TextureNameMatchMode.Regex)
			{
				try
				{
					// The engine timeout bounds each match, including nested and alternation backtracking.
					regex = new Regex(query ?? string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
						TimeSpan.FromMilliseconds(25));
				}
				catch(ArgumentException e) { Error = e.Message; }
			}
		}

		public bool Match(string name, out int score)
		{
			score = 0;
			if(Error != null || name == null) return false;
			if(mode == TextureNameMatchMode.Regex)
			{
				try { return regex.IsMatch(name); }
				catch(RegexMatchTimeoutException)
				{
					TimedOut = true;
					Error = "Pattern too slow.";
					return false;
				}
			}

			string uppername = name.ToUpperInvariant();
			bool substring = uppername.Contains(query);
			if(mode == TextureNameMatchMode.Name) return substring;
			if(tokens.Length == 0) return true;

			long total = 0;
			foreach(string token in tokens)
			{
				long tokenScore;
				if(!MatchToken(uppername, token, out tokenScore)) return false;
				total += tokenScore;
			}

			// Separate score bands make substring priority independent of name/query length.
			score = (substring ? 1000000 : 0) + 500000 + (int)Math.Max(-499999L, Math.Min(499999L, total));
			return true;
		}

		private static bool MatchToken(string name, string token, out long score)
		{
			score = 0;
			if(token.Length > name.Length) return false;
			long[] previous = new long[name.Length];
			long[] current = new long[name.Length];
			const long missing = long.MinValue / 2;
			for(int i = 0; i < token.Length; i++)
			{
				long bestSkipped = missing;
				for(int j = 0; j < name.Length; j++)
				{
					current[j] = missing;
					if(i > 0 && j > 1 && previous[j - 2] != missing)
						bestSkipped = Math.Max(bestSkipped, previous[j - 2] + j - 2);
					if(name[j] != token[i]) continue;
					long before = i == 0 ? -j : missing;
					if(i > 0)
					{
						if(j > 0 && previous[j - 1] != missing) before = previous[j - 1] + 20;
						if(bestSkipped != missing) before = Math.Max(before, bestSkipped - j + 1);
					}
					if(before != missing) current[j] = before + 16 + (IsBoundary(name, j) ? 12 : 0);
				}
				long[] swap = previous;
				previous = current;
				current = swap;
			}
			score = missing;
			foreach(long candidate in previous) score = Math.Max(score, candidate);
			return score != missing;
		}

		private static bool IsBoundary(string name, int index)
		{
			if(index == 0) return true;
			char before = name[index - 1], current = name[index];
			return before == '/' || before == '\\' || before == '_' || before == '-' || before == '.'
				|| char.IsWhiteSpace(before) || (char.IsLetter(before) && char.IsDigit(current))
				|| (char.IsDigit(before) && char.IsLetter(current));
		}
	}
}
