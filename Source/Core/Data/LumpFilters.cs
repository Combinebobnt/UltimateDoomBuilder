#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using CodeImp.DoomBuilder.ZDoom;

#endregion

namespace CodeImp.DoomBuilder.Data
{
	// The engine's filter/<name>/ rules (UZDoom d_main.cpp D_InitGame, resourcefile.cpp PostProcessArchive)
	internal static class LumpFilters
	{
		#region ================== Constants

		private const string IWADINFO_RESOURCE = "iwadinfo.txt";

		// GAME_* bits from UZDoom gamedata/gametype.h, in GameNames order
		private const int GAME_DOOM = 1;
		private const int GAME_HERETIC = 2;
		private const int GAME_HEXEN = 4;
		private const int GAME_STRIFE = 8;
		private const int GAME_CHEX = 16;
		private static readonly string[] GAMENAMES = { "Doom", "Heretic", "Hexen", "Strife", "Chex" };
		private static readonly int[] GAMEMASKS = { GAME_DOOM, GAME_HERETIC, GAME_HEXEN, GAME_STRIFE, GAME_CHEX };

		#endregion

		#region ================== Variables

		private static List<IWadInfo> bundled;
		private static readonly object bundledlock = new object();

		#endregion

		#region ================== Methods

		// The engine's gameTypeFilter list, each dotted name expanded to its prefixes, first occurrence kept
		public static List<string> BuildFilterNames(string game, string autoname)
		{
			List<string> names = new List<string>();
			int index = GameIndex(game);

			if(index != -1)
			{
				int mask = GAMEMASKS[index];
				if((mask & (GAME_HERETIC | GAME_HEXEN)) != 0) AddName(names, "game-Raven");
				if((mask & (GAME_DOOM | GAME_STRIFE | GAME_CHEX)) != 0) AddName(names, "game-DoomStrifeChex");
				if((mask & (GAME_DOOM | GAME_CHEX)) != 0) AddName(names, "game-DoomChex");
				AddName(names, "game-" + GAMENAMES[index]);
			}

			if(!string.IsNullOrEmpty(autoname))
			{
				AddWithPrefixes(names, autoname);

				// The engine's workaround for old Doom filter names, Mid(12) included
				if(autoname.IndexOf("doom.id.doom", StringComparison.Ordinal) >= 0)
					AddWithPrefixes(names, "doom.doom" + autoname.Substring(12));
			}

			return names;
		}

		// The first entry whose every MustContain name is present (d_iwad.cpp ScanIWAD), else null
		public static IWadInfo IdentifyIWad(ICollection<string> names, IList<IWadInfo> table)
		{
			HashSet<string> present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
			foreach(IWadInfo info in table)
			{
				// The engine matches an entry without MustContain to anything; a stray one from a loaded mod must not
				if(info.MustContain.Count == 0) continue;

				bool all = true;
				foreach(string lump in info.MustContain)
				{
					if(!present.Contains(lump))
					{
						all = false;
						break;
					}
				}

				if(all) return info;
			}

			return null;
		}

		// The engine's game name for a game configuration's basegame, or null
		public static string GameFromBaseGame(string basegame)
		{
			int index = GameIndex(basegame);
			return (index == -1 ? null : GAMENAMES[index]);
		}

		// UZDoom's stock IWADINFO table, parsed once
		public static List<IWadInfo> GetBundledIWadInfos()
		{
			lock(bundledlock)
			{
				if(bundled == null) bundled = LoadBundledIWadInfos();
				return bundled;
			}
		}

		private static List<IWadInfo> LoadBundledIWadInfos()
		{
			foreach(string rn in typeof(LumpFilters).Assembly.GetManifestResourceNames())
			{
				if(!rn.EndsWith(IWADINFO_RESOURCE, StringComparison.OrdinalIgnoreCase)) continue;

				using(Stream stream = typeof(LumpFilters).Assembly.GetManifestResourceStream(rn))
				{
					MemoryStream data = new MemoryStream();
					stream.CopyTo(data);

					IWadInfoParser parser = new IWadInfoParser();
					parser.Parse(new TextResourceData(data, new DataLocation(DataLocation.RESOURCE_DIRECTORY, string.Empty, false, false, false, new List<string>()), IWADINFO_RESOURCE), false);
					if(parser.HasError)
					{
						General.WriteLogLine("Unable to parse the bundled " + IWADINFO_RESOURCE + ", lump filters will use game names only");
						return new List<IWadInfo>();
					}

					return parser.IWads;
				}
			}

			General.WriteLogLine("The bundled " + IWADINFO_RESOURCE + " is missing, lump filters will use game names only");
			return new List<IWadInfo>();
		}

		private static int GameIndex(string game)
		{
			if(string.IsNullOrEmpty(game)) return -1;
			for(int i = 0; i < GAMENAMES.Length; i++)
				if(string.Equals(GAMENAMES[i], game, StringComparison.OrdinalIgnoreCase)) return i;
			return -1;
		}

		// "a.b.c" adds "a", "a.b" and "a.b.c", as the engine tries each prefix in turn
		private static void AddWithPrefixes(List<string> names, string name)
		{
			for(int pos = name.IndexOf('.'); pos != -1; pos = name.IndexOf('.', pos + 1))
				AddName(names, name.Substring(0, pos));
			AddName(names, name);
		}

		// A repeated name matches nothing new in the engine, since the earlier pass already moved its lumps
		private static void AddName(List<string> names, string name)
		{
			if(name.Length == 0) return;
			foreach(string n in names)
				if(string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return;
			names.Add(name);
		}

		#endregion
	}
}
