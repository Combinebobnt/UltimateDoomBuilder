#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Map.Statistics
{
	public enum TallyGameMode
	{
		Single,
		Cooperative,
		Deathmatch
	}

	// Answers whether a placed thing spawns for a skill, game mode and player class, in UDMF terms
	public static class ThingSpawnFilter
	{
		public const string SINGLE = "single";
		public const string COOP = "coop";
		public const string DM = "dm";

		// The names one per-thing "friendly" flag goes by across formats
		public static readonly string[] FRIENDLY_FLAGS = { "friend", "friendly", "strifeally" };

		// Builds the UDMF view of a thing's flags without touching the thing
		public static Dictionary<string, bool> UdmfView(bool isudmf, IDictionary<string, bool> flags, IEnumerable<FlagTranslation> translation)
		{
			Dictionary<string, bool> view = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

			if(isudmf)
			{
				foreach(KeyValuePair<string, bool> f in flags) view[f.Key] = f.Value;
				return view;
			}

			// Same bits-from-keys step as Thing.TranslateToUDMF
			int bits = 0;
			foreach(KeyValuePair<string, bool> f in flags)
				if(f.Value && int.TryParse(f.Key, out int flagbit)) bits |= flagbit;

			if(translation != null)
			{
				foreach(FlagTranslation ft in translation)
				{
					bool set = (bits & ft.Flag) == ft.Flag;
					if(set) bits &= ~ft.Flag;
					for(int i = 0; i < ft.Fields.Count; i++)
						view[ft.Fields[i]] = set ? ft.FieldValues[i] : !ft.FieldValues[i];
				}
			}

			// A mode the format can't express is always on (Doom has only "!single")
			foreach(string mode in new[] { SINGLE, COOP, DM })
				if(!view.ContainsKey(mode)) view[mode] = true;

			return view;
		}

		// cls is the 1-based player class for Hexen, 0 when classes don't filter
		public static bool SpawnsIn(IDictionary<string, bool> view, int skill, TallyGameMode mode, int cls)
		{
			if(!Get(view, "skill" + skill)) return false;
			if(!Get(view, ModeField(mode))) return false;
			if(cls > 0 && mode != TallyGameMode.Deathmatch && !Get(view, "class" + cls)) return false;
			return true;
		}

		public static bool IsFriendly(IDictionary<string, bool> view)
		{
			foreach(string name in FRIENDLY_FLAGS)
				if(Get(view, name)) return true;
			return false;
		}

		public static bool Get(IDictionary<string, bool> view, string name)
		{
			return view.TryGetValue(name, out bool value) && value;
		}

		public static string ModeField(TallyGameMode mode)
		{
			switch(mode)
			{
				case TallyGameMode.Cooperative: return COOP;
				case TallyGameMode.Deathmatch: return DM;
				default: return SINGLE;
			}
		}

		// LoadSkills reads a Hashtable, so config order isn't skill order
		public static List<SkillInfo> SortedSkills(IEnumerable<SkillInfo> skills)
		{
			return skills.OrderBy(s => s.Index).ToList();
		}

		// Classes only filter in Hexen itself; other games' player classes have spawn mask 0
		public static bool UsesPlayerClasses(string basegame)
		{
			return string.Equals(basegame, "Hexen", StringComparison.OrdinalIgnoreCase);
		}
	}
}
