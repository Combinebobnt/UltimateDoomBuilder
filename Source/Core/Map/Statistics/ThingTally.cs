#region ================== Namespaces

using System;
using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Map.Statistics
{
	// One placed thing as the tally sees it
	public struct TallyThing
	{
		public int Index;
		public ThingTallyInfo Info;
		public IDictionary<string, bool> Flags;

		public TallyThing(int index, ThingTallyInfo info, IDictionary<string, bool> flags)
		{
			Index = index;
			Info = info;
			Flags = flags;
		}
	}

	// The totals for one skill, plus which things are behind the clickable rows
	public sealed class TallyColumn
	{
		public int Skill;
		public int Kills;
		public int Items;
		public int SecretThings;
		public int Unresolved;
		public long MonsterHealth;
		public long Health;
		public long Armor;
		public Dictionary<string, long> Ammo = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		public List<int> KillThings = new List<int>();
		public List<int> ItemThings = new List<int>();
		public List<int> SecretThingList = new List<int>();
		public List<int> UnresolvedThings = new List<int>();
	}

	public static class ThingTally
	{
		public static List<TallyColumn> Compute(IEnumerable<TallyThing> things, IList<int> skills, TallyGameMode mode, int cls)
		{
			List<TallyColumn> columns = new List<TallyColumn>(skills.Count);
			foreach(int skill in skills) columns.Add(new TallyColumn { Skill = skill });

			foreach(TallyThing t in things)
			{
				bool friendly = ThingSpawnFilter.IsFriendly(t.Flags);
				bool nocount = ThingSpawnFilter.Get(t.Flags, "nocount");
				bool secretflag = ThingSpawnFilter.Get(t.Flags, "countsecret");

				foreach(TallyColumn col in columns)
				{
					if(!ThingSpawnFilter.SpawnsIn(t.Flags, col.Skill, mode, cls)) continue;
					Add(col, t, friendly, nocount, secretflag);
				}
			}

			return columns;
		}

		private static void Add(TallyColumn col, TallyThing t, bool friendly, bool nocount, bool secretflag)
		{
			ThingTallyInfo info = t.Info;

			if(secretflag || (info != null && info.CountSecret == true))
			{
				col.SecretThings++;
				col.SecretThingList.Add(t.Index);
			}

			if(info == null || info.IsUnresolved)
			{
				col.Unresolved++;
				col.UnresolvedThings.Add(t.Index);
				return;
			}

			// The engine's CountsAsKill excludes friendlies, and nocount clears both counts
			if(info.CountKill == true && info.Friendly != true && !friendly && !nocount)
			{
				col.Kills++;
				col.KillThings.Add(t.Index);
				if(info.SpawnHealth.HasValue) col.MonsterHealth += info.SpawnHealth.Value;
			}

			if(info.CountItem == true && !nocount)
			{
				col.Items++;
				col.ItemThings.Add(t.Index);
			}

			if(info.HealthAmount.HasValue) col.Health += info.HealthAmount.Value;
			if(info.ArmorAmount.HasValue) col.Armor += info.ArmorAmount.Value;
			if(info.Ammo != null)
			{
				foreach(KeyValuePair<string, int> a in info.Ammo)
				{
					col.Ammo.TryGetValue(a.Key, out long current);
					col.Ammo[a.Key] = current + a.Value;
				}
			}
		}
	}
}
