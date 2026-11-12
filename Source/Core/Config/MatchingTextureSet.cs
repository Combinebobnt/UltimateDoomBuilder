
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Text;
using CodeImp.DoomBuilder.Data;
using System.Text.RegularExpressions;

#endregion

namespace CodeImp.DoomBuilder.Config
{
	public sealed class MatchingTextureSet : TextureSet, IFilledTextureSet, IComparable<MatchingTextureSet>
	{
		#region ================== Constants

		internal enum FilterKind { Exact, Prefix, Pattern }

		private const uint FNV_OFFSET_BASIS = 2166136261;
		private const uint FNV_PRIME = 16777619;
		private const byte HASH_LIST_SEPARATOR = 0xFF;

		#endregion

		#region ================== Variables

		// Never stored, only used at run-time
		private Regex regex;
		
		// Matching textures and flats
		private List<ImageData> textures;
		private List<ImageData> flats;
		
		#endregion

		#region ================== Properties

		public ICollection<ImageData> Textures { get { return textures; } }
		public ICollection<ImageData> Flats { get { return flats; } } //mxd

		#endregion
		
		#region ================== Constructor / Destructor
		
		// New texture set for quick matching
		public MatchingTextureSet(ICollection<string> filters)
		{
			this.filters = new List<string>(filters);
			
			// Setup
			Setup();
		}
		
		// Texture set from defined set
		internal MatchingTextureSet(DefinedTextureSet definedset)
		{
			// Copy the name
			this.name = definedset.Name;
			
			// Copy the filters
			this.filters = new List<string>(definedset.Filters);
			
			// Setup
			Setup();
		}
		
		#endregion
		
		#region ================== Methods
		
		// This sets up the object
		private void Setup()
		{
			// Make the regex string that handles all filters
			StringBuilder regexstr = new StringBuilder("");
			foreach(string s in this.filters)
			{
				// Make sure filter is in uppercase
				string ss = s.ToUpperInvariant();

				// Escape regex characters
				ss = ss.Replace("+", "\\+");
				ss = ss.Replace("\\", "\\\\");
				ss = ss.Replace("|", "\\|");
				ss = ss.Replace("{", "\\{");
				ss = ss.Replace("[", "\\[");
				ss = ss.Replace("(", "\\(");
				ss = ss.Replace(")", "\\)");
				ss = ss.Replace("^", "\\^");
				ss = ss.Replace("$", "\\$");
				ss = ss.Replace(".", "\\.");
				ss = ss.Replace("#", "\\#");
				ss = ss.Replace(" ", "\\ ");

				// Replace the ? with the regex code for single character
				ss = ss.Replace("?", ".");

				// Replace the * with the regex code for optional multiple characters
				ss = ss.Replace("*", ".*?");

				// When a filter has already added, insert a conditional OR operator
				if(regexstr.Length > 0) regexstr.Append("|");

				// Open group without backreferencing
				regexstr.Append("(?:");

				// Must be start of string
				regexstr.Append("\\A");

				// Add the filter
				regexstr.Append(ss);

				// Must be end of string
				regexstr.Append("\\Z");

				// Close group
				regexstr.Append(")");
			}

			// No filters added? Then make a never-matching regex
			if(this.filters.Count == 0) regexstr.Append("\\Z\\A");
			
			// Make the regex
			regex = new Regex(regexstr.ToString(), RegexOptions.Compiled |
												   RegexOptions.CultureInvariant);

			// Initialize collections
			textures = new List<ImageData>();
			flats = new List<ImageData>();
		}
		
		// This matches a name against the regex and adds a texture to
		// the list if it matches. Returns true when matched and added.
		internal bool AddTexture(ImageData image)
		{
			// Check against regex
			if(regex.IsMatch(image.ShortName.ToUpperInvariant()))
			{
				// Matches! Add it.
				textures.Add(image);
				return true;
			}
			else
			{
				// Doesn't match
				return false;
			}
		}

		// This matches a name against the regex and adds a flat to
		// the list if it matches. Returns true when matched and added.
		internal bool AddFlat(ImageData image)
		{
			// Check against regex
			if(regex.IsMatch(image.ShortName.ToUpperInvariant()))
			{
				// Matches! Add it.
				flats.Add(image);
				return true;
			}
			else
			{
				// Doesn't match
				return false;
			}
		}
		
		// This only checks if the given image is a match
		internal bool IsMatch(ImageData image)
		{
			return regex.IsMatch(image.ShortName.ToUpperInvariant());
		}

		// This only checks if the given texture name is a match (mxd)
		public bool IsMatch(string texturename) 
		{
			return regex.IsMatch(texturename.ToUpperInvariant());
		}

		// This compares it for sorting
		public int CompareTo(MatchingTextureSet other)
		{
			return string.Compare(this.name, other.name);
		}

		// Exact and prefix filters are printable ASCII without * ? + or \ (prefix: before one trailing *)
		internal static FilterKind ClassifyFilter(string filter, out string literal)
		{
			string upper = filter.ToUpperInvariant();
			if(IsLiteralFilterPart(upper, upper.Length))
			{
				literal = upper;
				return FilterKind.Exact;
			}

			if(upper.Length > 0 && upper[upper.Length - 1] == '*' && IsLiteralFilterPart(upper, upper.Length - 1))
			{
				literal = upper.Substring(0, upper.Length - 1);
				return FilterKind.Prefix;
			}

			literal = upper;
			return FilterKind.Pattern;
		}

		private static bool IsLiteralFilterPart(string upper, int length)
		{
			for(int i = 0; i < length; i++)
			{
				char c = upper[i];
				if(c < '\x21' || c > '\x7E' || c == '*' || c == '?' || c == '+' || c == '\\') return false;
			}
			return true;
		}

		internal uint MembershipHash()
		{
			return HashLongNames(textures.ConvertAll(i => i.LongName), flats.ConvertAll(i => i.LongName));
		}

		// FNV-1a over each long name's 8 little-endian bytes, with one separator byte between the lists
		internal static uint HashLongNames(IEnumerable<long> texturenames, IEnumerable<long> flatnames)
		{
			uint hash = FNV_OFFSET_BASIS;
			foreach(long n in texturenames) hash = HashLong(hash, n);
			hash = HashByte(hash, HASH_LIST_SEPARATOR);
			foreach(long n in flatnames) hash = HashLong(hash, n);
			return hash;
		}

		private static uint HashLong(uint hash, long value)
		{
			for(int i = 0; i < 8; i++) hash = HashByte(hash, (byte)(value >> (i * 8)));
			return hash;
		}

		private static uint HashByte(uint hash, byte value)
		{
			return unchecked((hash ^ value) * FNV_PRIME);
		}

		#endregion
	}
}
