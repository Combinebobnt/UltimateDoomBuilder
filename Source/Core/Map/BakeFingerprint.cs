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
	// (no index comments), keeping only the things the baker reads, in map order.
	internal static class BakeFingerprint
	{
		// ZDRay fd3c6bb reads these fixed numbers (src/level/doomdata.h, level_light.cpp, level.cpp
		// GetPolySpots); slope things are kept because the engine applies them. Re-derive on a baker change.
		public static readonly HashSet<int> BAKER_THING_TYPES = new HashSet<int> {
			9875, 9876, 9881, 9890, // Light probe, point light, spotlight, ZDRayInfo
			3000, 9300, 9301, 9302, 9303, // Polyobject anchors and spawn spots
			1500, 1501, 1504, 1505, 9500, 9501, 9502, 9503, 9510, 9511 // Slopes and vertex heights
		};

		// Hexen polyobject spawn spots, read only when a 3000 anchor exists; otherwise Doom's Imp and Demon
		public static readonly HashSet<int> HEXEN_POLY_SPAWN_TYPES = new HashSet<int> { 3001, 3002 };
		private const int HEXEN_POLY_ANCHOR_TYPE = 3000;

		// Returns null when the TEXTMAP does not parse
		public static string Compute(string[] textmap)
		{
			UniversalParser parser = new UniversalParser();
			if(!parser.InputConfiguration(textmap)) return null;

			bool hexenpolyobjects = false;
			foreach(UniversalEntry entry in parser.Root)
			{
				if(IsThing(entry) && ThingType((UniversalCollection)entry.Value) == HEXEN_POLY_ANCHOR_TYPE)
				{
					hexenpolyobjects = true;
					break;
				}
			}

			UniversalParser kept = new UniversalParser();
			foreach(UniversalEntry entry in parser.Root)
			{
				if(IsThing(entry))
				{
					int type = ThingType((UniversalCollection)entry.Value);
					if(!BAKER_THING_TYPES.Contains(type) && !(hexenpolyobjects && HEXEN_POLY_SPAWN_TYPES.Contains(type))) continue;
				}
				kept.Root.Add(entry);
			}

			byte[] data = UniversalStreamReader.ENCODING.GetBytes(kept.OutputConfiguration());
			using(SHA256 sha = SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
			}
		}

		// Folds the emissive table's digest in; no table keeps the TEXTMAP fingerprint as it was
		public static string Combine(string textmapfingerprint, string emissivedigest)
		{
			if(textmapfingerprint == null || string.IsNullOrEmpty(emissivedigest)) return textmapfingerprint;
			using(SHA256 sha = SHA256.Create())
			{
				byte[] data = Encoding.UTF8.GetBytes(textmapfingerprint + "\nemissive " + emissivedigest);
				return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
			}
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
