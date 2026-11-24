#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// Identifies the map a lightmap was baked from. Hashes the TEXTMAP as the UDMF parser re-emits it
	// (no index comments), minus things that cannot change geometry or light but move during testing.
	internal static class BakeFingerprint
	{
		// Player 1 start, the only thing Test from current position moves or creates
		public const int PLAYER1_START_TYPE = 1;

		// Returns null when the TEXTMAP does not parse
		public static string Compute(string[] textmap, ICollection<int> excludedtypes)
		{
			UniversalParser parser = new UniversalParser();
			if(!parser.InputConfiguration(textmap)) return null;

			UniversalParser kept = new UniversalParser();
			foreach(UniversalEntry entry in parser.Root)
			{
				if(IsThing(entry) && excludedtypes.Contains(ThingType((UniversalCollection)entry.Value))) continue;
				kept.Root.Add(entry);
			}

			byte[] data = UniversalStreamReader.ENCODING.GetBytes(kept.OutputConfiguration());
			using(SHA256 sha = SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
			}
		}

		// The thing types a fingerprint leaves out for this configuration
		public static ICollection<int> ExcludedTypes(int start3dmodethingtype)
		{
			HashSet<int> types = new HashSet<int> { PLAYER1_START_TYPE };
			if(start3dmodethingtype != 0) types.Add(start3dmodethingtype);
			return types;
		}

		internal static bool IsThing(UniversalEntry entry)
		{
			return entry.Value is UniversalCollection && string.Equals(entry.Key, "thing", StringComparison.OrdinalIgnoreCase);
		}

		private static int ThingType(UniversalCollection thing)
		{
			foreach(UniversalEntry field in thing)
				if(string.Equals(field.Key, "type", StringComparison.OrdinalIgnoreCase) && field.Value is int) return (int)field.Value;
			return 0;
		}
	}

	// The baker's TEXTMAP, renumbered to match its ZNODES and LIGHTMAP, with the current map's things
	internal static class BakedTextmap
	{
		// Keeps every non-thing entry of the baked TEXTMAP in order and puts the current things, in
		// their order, where the baked ones started. Nothing in ZNODES or LIGHTMAP indexes a thing.
		// Returns null when either TEXTMAP does not parse.
		public static string ReplaceThings(string[] baked, string[] current)
		{
			UniversalParser bakedparser = new UniversalParser();
			if(!bakedparser.InputConfiguration(baked)) return null;
			UniversalParser currentparser = new UniversalParser();
			if(!currentparser.InputConfiguration(current)) return null;

			List<UniversalEntry> things = new List<UniversalEntry>();
			foreach(UniversalEntry entry in currentparser.Root)
				if(BakeFingerprint.IsThing(entry)) things.Add(entry);

			UniversalParser result = new UniversalParser();
			bool placed = false;
			foreach(UniversalEntry entry in bakedparser.Root)
			{
				if(BakeFingerprint.IsThing(entry))
				{
					if(!placed) result.Root.AddRange(things);
					placed = true;
					continue;
				}
				result.Root.Add(entry);
			}
			if(!placed) result.Root.AddRange(things);

			return result.OutputConfiguration();
		}

		internal static string[] ReadLines(Lump lump)
		{
			lump.Stream.Seek(0, SeekOrigin.Begin);
			return UniversalStreamReader.ReadLines(lump.Stream);
		}

		internal static string[] Lines(string text)
		{
			return UniversalStreamReader.ReadLines(new MemoryStream(UniversalStreamReader.ENCODING.GetBytes(text)));
		}
	}
}
