#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Dehacked;
using CodeImp.DoomBuilder.ZDoom;

#endregion

namespace CodeImp.DoomBuilder.Map.Statistics
{
	// One class of an actor's inheritance chain, as the chain walk reads it
	public sealed class ActorLink
	{
		public string ClassName;
		public IDictionary<string, bool> Flags;
		public IDictionary<string, List<string>> Props;

		public ActorLink(string classname, IDictionary<string, bool> flags, IDictionary<string, List<string>> props)
		{
			ClassName = classname ?? string.Empty;
			Flags = flags ?? new Dictionary<string, bool>();
			Props = props ?? new Dictionary<string, List<string>>();
		}
	}

	// Resolves each thing type's count flags and amounts, in the plan's route order
	public sealed class ThingTallyResolver
	{
		#region ================== Constants

		public const int DEFAULT_HEALTH = 1000;
		public const string ROUTE_PATCH = "DEHACKED";
		public const string ROUTE_ACTOR = "actor";
		public const string ROUTE_CONFIG = "config dehacked";
		public const string ROUTE_TABLE = "vanilla table";
		public const string ROUTE_EDITOR = "map special";

		#endregion

		#region ================== Variables

		private readonly Dictionary<int, ThingTallyInfo> cache = new Dictionary<int, ThingTallyInfo>();
		private readonly Dictionary<int, DehackedThing> patchthings = new Dictionary<int, DehackedThing>();
		private readonly Dictionary<int, DehackedThing> basethings = new Dictionary<int, DehackedThing>();
		private readonly DehackedData dehackeddata;
		private readonly bool doombase;

		#endregion

		#region ================== Constructor

		// Uses the open map's config and resources
		public ThingTallyResolver()
		{
			dehackeddata = General.Map.Config.DehackedData;
			doombase = string.Equals(General.Map.Config.BaseGame, "Doom", StringComparison.OrdinalIgnoreCase);

			if(dehackeddata != null)
			{
				foreach(DehackedThing t in dehackeddata.Things.Values)
					if(t.Props.TryGetValue("id #", out string id) && int.TryParse(id, out int ednum) && !basethings.ContainsKey(ednum))
						basethings[ednum] = t;
			}

			foreach(DehackedThing t in General.Map.Data.DehackedThings)
				if(t.HasDoomEdNum) patchthings[t.DoomEdNum] = t;
		}

		#endregion

		#region ================== Methods

		public ThingTallyInfo Resolve(int type)
		{
			if(cache.TryGetValue(type, out ThingTallyInfo info)) return info;

			info = new ThingTallyInfo();
			ThingTypeInfo ti = General.Map.Data.GetThingInfoEx(type);

			// Player starts, polyobject spots and the like are engine map specials, never counted
			if(ti != null && !string.IsNullOrEmpty(ti.ClassName) && ti.ClassName.StartsWith("$"))
			{
				info.FillFrom(NotCounted(), ROUTE_EDITOR);
			}
			else
			{
				if(patchthings.TryGetValue(type, out DehackedThing patch))
				{
					DehackedThing patchbase = null;
					dehackeddata?.Things.TryGetValue(patch.Number, out patchbase);
					info.FillFrom(FromPatch(patch, patchbase), ROUTE_PATCH);
				}

				if(ti != null)
				{
					ActorStructure actor = ti.Actor ?? (string.IsNullOrEmpty(ti.ClassName) ? null : General.Map.Data.GetZDoomActor(ti.ClassName));
					if(actor != null)
					{
						List<ActorLink> chain = BuildChain(actor, out bool complete);
						info.FillFrom(WalkChain(chain, complete, AmmoGroup), ROUTE_ACTOR);
					}
				}

				// Config base data only for a type the actor route didn't resolve
				if(info.IsUnresolved && basethings.TryGetValue(type, out DehackedThing basething))
					info.FillFrom(FromConfigBase(basething, dehackeddata.BitMnemonics), ROUTE_CONFIG);

				if(doombase) info.FillFrom(VanillaDoomAmounts.Get(type), ROUTE_TABLE);
			}

			cache[type] = info;
			return info;
		}

		private static ThingTallyInfo NotCounted()
		{
			ThingTallyInfo info = new ThingTallyInfo { CountKill = false, CountItem = false, CountSecret = false, Friendly = false };
			info.SetNoAmounts();
			return info;
		}

		// Most-derived first; complete when the walk reaches the root Actor
		internal static List<ActorLink> BuildChain(ActorStructure actor, out bool complete)
		{
			List<ActorLink> chain = new List<ActorLink>();
			HashSet<ActorStructure> seen = new HashSet<ActorStructure>();
			complete = false;

			for(ActorStructure a = actor; a != null && seen.Add(a); a = a.BaseClass)
			{
				chain.Add(new ActorLink(a.ClassName, a.Flags, a.Properties));
				if(string.Equals(a.ClassName, "Actor", StringComparison.OrdinalIgnoreCase) || a.skipsuper)
				{
					complete = true;
					break;
				}

				// A DECORATE actor with no parent inherits Actor implicitly
				if(a.BaseClass == null && !(a is ZScriptActorStructure) && string.Equals(a.InheritsClass, "actor", StringComparison.OrdinalIgnoreCase))
				{
					complete = true;
					break;
				}
			}

			return chain;
		}

		// Ammo is grouped by the class that directly inherits Ammo
		private static string AmmoGroup(string ammoclass)
		{
			ActorStructure a = General.Map.Data.GetZDoomActor(ammoclass);
			string previous = null;
			HashSet<ActorStructure> seen = new HashSet<ActorStructure>();
			for(; a != null && seen.Add(a); a = a.BaseClass)
			{
				if(string.Equals(a.ClassName, "Ammo", StringComparison.OrdinalIgnoreCase)) return previous ?? ammoclass;
				previous = a.ClassName;
			}
			return ammoclass;
		}

		// The pure chain walk: first class that sets a field wins
		public static ThingTallyInfo WalkChain(IList<ActorLink> chain, bool complete, Func<string, string> ammogroup)
		{
			ThingTallyInfo info = new ThingTallyInfo();

			HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach(ActorLink link in chain) names.Add(link.ClassName);

			// What a spawner spawns is what the engine counts
			if(names.Contains("RandomSpawner")) return info;

			// An unreadable health (a ZScript expression) stays unresolved even on a complete chain
			bool healthseen = false;

			foreach(ActorLink link in chain)
			{
				if(!info.CountKill.HasValue)
				{
					if(link.Flags.TryGetValue("countkill", out bool ck)) info.CountKill = ck;
					else if(link.Props.ContainsKey("monster")) info.CountKill = true;
				}

				if(!info.CountItem.HasValue && link.Flags.TryGetValue("countitem", out bool ci)) info.CountItem = ci;
				if(!info.CountSecret.HasValue && link.Flags.TryGetValue("countsecret", out bool cs)) info.CountSecret = cs;
				if(!info.Friendly.HasValue && link.Flags.TryGetValue("friendly", out bool fr)) info.Friendly = fr;
				if(!healthseen && link.Props.TryGetValue("health", out List<string> hv))
				{
					healthseen = true;
					info.SpawnHealth = ParseHealth(hv);
				}
			}

			if(complete)
			{
				if(!info.CountKill.HasValue) info.CountKill = false;
				if(!info.CountItem.HasValue) info.CountItem = false;
				if(!info.CountSecret.HasValue) info.CountSecret = false;
				if(!info.Friendly.HasValue) info.Friendly = false;
				if(!healthseen) info.SpawnHealth = DEFAULT_HEALTH;
				WalkAmounts(chain, names, info, ammogroup ?? (s => s));
			}

			return info;
		}

		private static void WalkAmounts(IList<ActorLink> chain, HashSet<string> names, ThingTallyInfo info, Func<string, string> ammogroup)
		{
			if(!names.Contains("Inventory"))
			{
				info.SetNoAmounts();
				return;
			}

			if(names.Contains("Health"))
			{
				int? amount = FirstInt(chain, "inventory.amount");
				if(!amount.HasValue) return;
				info.SetNoAmounts();
				info.HealthAmount = amount;
			}
			else if(names.Contains("BasicArmorPickup") || names.Contains("BasicArmorBonus"))
			{
				int? amount = FirstInt(chain, "armor.saveamount");
				if(!amount.HasValue) return;
				info.SetNoAmounts();
				info.ArmorAmount = amount;
			}
			else if(names.Contains("Ammo"))
			{
				int? amount = FirstInt(chain, "inventory.amount");
				string group = null;
				for(int i = 1; i < chain.Count; i++)
					if(string.Equals(chain[i].ClassName, "Ammo", StringComparison.OrdinalIgnoreCase)) group = chain[i - 1].ClassName;
				if(!amount.HasValue || group == null) return;
				info.SetNoAmounts();
				info.AddAmmo(group, amount.Value);
			}
			else if(names.Contains("Weapon"))
			{
				info.SetNoAmounts();
				for(int slot = 1; slot <= 2; slot++)
				{
					string type = FirstString(chain, "weapon.ammotype" + slot);
					int? give = FirstInt(chain, "weapon.ammogive" + slot);
					if(!string.IsNullOrEmpty(type) && give.HasValue && give.Value > 0)
						info.AddAmmo(ammogroup(type), give.Value);
				}
			}

			// CustomInventory and other inventory leave amounts unresolved, for the table to fill
		}

		private static int? ParseHealth(List<string> values)
		{
			if(values == null || values.Count == 0) return null;
			string v = values[0].Trim();
			if(string.Equals(v, "DEFAULT_HEALTH", StringComparison.OrdinalIgnoreCase)) return DEFAULT_HEALTH;
			if(int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return h;
			return null;
		}

		private static string FirstString(IList<ActorLink> chain, string prop)
		{
			foreach(ActorLink link in chain)
				if(link.Props.TryGetValue(prop, out List<string> v) && v.Count > 0)
					return v[0].Trim().Trim('"');
			return null;
		}

		private static int? FirstInt(IList<ActorLink> chain, string prop)
		{
			string v = FirstString(chain, prop);
			if(v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
			return null;
		}

		// Only the fields the patch itself sets: Process copied the rest from the base thing
		public static ThingTallyInfo FromPatch(DehackedThing patch, DehackedThing basething)
		{
			ThingTallyInfo info = new ThingTallyInfo();

			if(SetByPatch(patch, basething, "bits"))
			{
				info.CountKill = patch.Bits.Contains("countkill");
				info.CountItem = patch.Bits.Contains("countitem");
				info.Friendly = patch.Bits.Contains("friend") || patch.Bits.Contains("friendly");
			}

			if(SetByPatch(patch, basething, "hit points") && int.TryParse(patch.Props["hit points"], out int hp))
				info.SpawnHealth = hp;

			return info;
		}

		private static bool SetByPatch(DehackedThing patch, DehackedThing basething, string key)
		{
			if(!patch.Props.TryGetValue(key, out string value)) return false;
			if(basething == null || !basething.Props.TryGetValue(key, out string basevalue)) return true;
			return value != basevalue;
		}

		// Config base things never go through Process, so their bits are still a decimal string
		public static ThingTallyInfo FromConfigBase(DehackedThing basething, Dictionary<long, string> mnemonics)
		{
			ThingTallyInfo info = new ThingTallyInfo();
			if(!basething.Props.TryGetValue("bits", out string s) || !long.TryParse(s, out long bits)) return info;

			HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach(KeyValuePair<long, string> m in mnemonics)
				if((m.Key & bits) == m.Key) set.Add(m.Value);

			info.CountKill = set.Contains("countkill");
			info.CountItem = set.Contains("countitem");
			info.CountSecret = false;
			info.Friendly = set.Contains("friend") || set.Contains("friendly");
			return info;
		}

		#endregion
	}
}
