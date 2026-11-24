#region ================== Namespaces

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// Finds the maps a game configuration recognizes in a WAD's lump list
	internal static class MapLumpScanner
	{
		private static readonly Regex episodemapregex = new Regex("^E[1-9]M[1-9]$");
		private static readonly Regex noepisodemapregex = new Regex("^MAP[0-9][0-9]$");

		// Map header names in lump order; with distinct, a repeated name is listed once
		public static List<string> FindMaps(Configuration cfg, WAD wad, bool distinct)
		{
			return FindMaps(cfg, LumpNames(wad), distinct);
		}

		public static List<string> FindMaps(Configuration cfg, IList<string> lumps, bool distinct)
		{
			List<string> mapnames = new List<string>();
			IDictionary maplumpnames = cfg.ReadSetting("maplumpnames", new Hashtable());
			int lumpsrequired = CountRequired(cfg, maplumpnames);

			for(int scanindex = 0; scanindex < (lumps.Count - 1); scanindex++)
			{
				if(maplumpnames.Contains(lumps[scanindex])) continue;

				string mapname = lumps[scanindex];
				if(CountFound(cfg, maplumpnames, lumps, scanindex) >= lumpsrequired && (!distinct || !mapnames.Contains(mapname)))
					mapnames.Add(mapname);
			}

			return mapnames;
		}

		// True when the configuration finds at least one map. Any lump named for the other episode format rejects the whole WAD.
		public static bool Matches(Configuration cfg, WAD wad)
		{
			return Matches(cfg, LumpNames(wad));
		}

		public static bool Matches(Configuration cfg, IList<string> lumps)
		{
			string mapnameformat = cfg.ReadSetting("mapnameformat", "");
			IDictionary maplumpnames = cfg.ReadSetting("maplumpnames", new Hashtable());
			int lumpsrequired = CountRequired(cfg, maplumpnames);

			for(int scanindex = 0; scanindex < (lumps.Count - 1); scanindex++)
			{
				if(MapNameFormatMismatch(mapnameformat, lumps[scanindex])) return false;
				if(maplumpnames.Contains(lumps[scanindex])) continue;
				if(CountFound(cfg, maplumpnames, lumps, scanindex) >= lumpsrequired) return true;
			}

			return false;
		}

		private static List<string> LumpNames(WAD wad)
		{
			List<string> names = new List<string>(wad.Lumps.Count);
			foreach(Lump lump in wad.Lumps) names.Add(lump.Name);
			return names;
		}

		// The map header is not counted: its name is the map's own
		private static int CountRequired(Configuration cfg, IDictionary maplumpnames)
		{
			int lumpsrequired = 0;
			foreach(DictionaryEntry ml in maplumpnames)
			{
				if(ml.Key.ToString() != MapManager.CONFIG_MAP_HEADER && cfg.ReadSetting("maplumpnames." + ml.Key + ".required", false))
					lumpsrequired++;
			}

			return lumpsrequired;
		}

		// Required lumps following the header, or -1 when one is forbidden in this map format
		private static int CountFound(Configuration cfg, IDictionary maplumpnames, IList<string> lumps, int scanindex)
		{
			int lumpsfound = 0;
			for(int checkoffset = 1; (scanindex + checkoffset) < lumps.Count && maplumpnames.Contains(lumps[scanindex + checkoffset]); checkoffset++)
			{
				string lumpname = lumps[scanindex + checkoffset];
				if(cfg.ReadSetting("maplumpnames." + lumpname + ".forbidden", false)) return -1;
				if(cfg.ReadSetting("maplumpnames." + lumpname + ".required", false)) lumpsfound++;
			}

			return lumpsfound;
		}

		private static bool MapNameFormatMismatch(string mapnameformat, string lumpname)
		{
			return (mapnameformat == MapManager.CONFIG_MAP_NAME_FORMAT_NO_EPISODE && episodemapregex.IsMatch(lumpname))
				|| (mapnameformat == MapManager.CONFIG_MAP_NAME_FORMAT_EPISODE && noepisodemapregex.IsMatch(lumpname));
		}
	}
}
