using System.Collections;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Map.Statistics;

namespace Builder.Tests
{
	public class MapStatisticsTallyTests
	{
		private static readonly int[] Skills = { 1, 2, 3, 4, 5 };

		private static List<FlagTranslation> Translation(params (int Bit, string Fields)[] entries)
		{
			return entries.Select(e => new FlagTranslation(new DictionaryEntry(e.Bit.ToString(), e.Fields))).ToList();
		}

		private static readonly List<FlagTranslation> Doom = Translation((1, "skill1,skill2"), (2, "skill3"), (4, "skill4,skill5"), (8, "ambush"), (16, "!single"));
		private static readonly List<FlagTranslation> Boom = Translation((1, "skill1,skill2"), (2, "skill3"), (4, "skill4,skill5"), (8, "ambush"), (16, "!single"), (32, "!dm"), (64, "!coop"));
		private static readonly List<FlagTranslation> Hexen = Translation((1, "skill1,skill2"), (2, "skill3"), (4, "skill4,skill5"), (8, "ambush"), (16, "dormant"),
			(32, "class1"), (64, "class2"), (128, "class3"), (256, "single"), (512, "coop"), (1024, "dm"));

		private static Dictionary<string, bool> Bits(int bits)
		{
			var flags = new Dictionary<string, bool>();
			for(int b = 1; b <= 1024; b <<= 1) flags[b.ToString()] = (bits & b) != 0;
			return flags;
		}

		private static string Spawns(IDictionary<string, bool> view, TallyGameMode mode, int cls = 0)
		{
			return string.Concat(Skills.Select(s => ThingSpawnFilter.SpawnsIn(view, s, mode, cls) ? "1" : "0"));
		}

		private static ThingTallyInfo Monster(int health) => new ThingTallyInfo { CountKill = true, CountItem = false, SpawnHealth = health };

		[Fact]
		public void DoomFormatModesAndSkills()
		{
			// Easy only, and multiplayer only at every skill
			var easy = ThingSpawnFilter.UdmfView(false, Bits(1), Doom);
			var mponly = ThingSpawnFilter.UdmfView(false, Bits(1 | 2 | 4 | 16), Doom);

			Assert.Equal("11000", Spawns(easy, TallyGameMode.Single));
			Assert.Equal("11000", Spawns(easy, TallyGameMode.Deathmatch));
			Assert.Equal("00000", Spawns(mponly, TallyGameMode.Single));
			Assert.Equal("11111", Spawns(mponly, TallyGameMode.Cooperative));
			Assert.Equal("11111", Spawns(mponly, TallyGameMode.Deathmatch));
		}

		[Fact]
		public void BoomNotDeathmatchAndNotCoop()
		{
			var notdm = ThingSpawnFilter.UdmfView(false, Bits(7 | 32), Boom);
			var notcoop = ThingSpawnFilter.UdmfView(false, Bits(7 | 64), Boom);

			Assert.Equal("11111", Spawns(notdm, TallyGameMode.Single));
			Assert.Equal("00000", Spawns(notdm, TallyGameMode.Deathmatch));
			Assert.Equal("11111", Spawns(notdm, TallyGameMode.Cooperative));
			Assert.Equal("00000", Spawns(notcoop, TallyGameMode.Cooperative));
			Assert.Equal("11111", Spawns(notcoop, TallyGameMode.Deathmatch));
		}

		[Fact]
		public void HexenModesAndClasses()
		{
			// Hard skills, single and deathmatch, fighter only
			var view = ThingSpawnFilter.UdmfView(false, Bits(4 | 32 | 256 | 1024), Hexen);

			Assert.Equal("00011", Spawns(view, TallyGameMode.Single, 1));
			Assert.Equal("00000", Spawns(view, TallyGameMode.Single, 2));
			Assert.Equal("00000", Spawns(view, TallyGameMode.Cooperative, 1));

			// Deathmatch skips the class check
			Assert.Equal("00011", Spawns(view, TallyGameMode.Deathmatch, 3));

			// Class 0 is what the window passes outside Hexen: class bits ignored
			Assert.True(ThingSpawnFilter.UsesPlayerClasses("hexen"));
			Assert.False(ThingSpawnFilter.UsesPlayerClasses("Doom"));
			Assert.Equal("00011", Spawns(view, TallyGameMode.Single, 0));
		}

		[Fact]
		public void UdmfAbsentFieldsAreFalse()
		{
			var flags = new Dictionary<string, bool> { { "skill2", true }, { "single", true } };
			var view = ThingSpawnFilter.UdmfView(true, flags, Doom);

			Assert.Equal("01000", Spawns(view, TallyGameMode.Single));
			Assert.Equal("00000", Spawns(view, TallyGameMode.Cooperative));
			Assert.Equal("00000", Spawns(view, TallyGameMode.Single, 1));
		}

		[Fact]
		public void PerThingFlagsAdjustCounts()
		{
			var all = new Dictionary<string, bool> { { "skill1", true }, { "skill2", true }, { "skill3", true }, { "skill4", true }, { "skill5", true }, { "single", true } };
			Dictionary<string, bool> With(string flag) => new Dictionary<string, bool>(all) { { flag, true } };
			var item = new ThingTallyInfo { CountKill = false, CountItem = true };

			var things = new List<TallyThing>
			{
				new TallyThing(0, Monster(60), all),
				new TallyThing(1, Monster(60), With("friend")),
				new TallyThing(2, Monster(60), With("friendly")),
				new TallyThing(3, Monster(60), With("strifeally")),
				new TallyThing(4, Monster(60), With("nocount")),
				new TallyThing(5, item, all),
				new TallyThing(6, item, With("nocount")),
				new TallyThing(7, new ThingTallyInfo { CountKill = false, CountItem = false }, With("countsecret")),
				new TallyThing(8, new ThingTallyInfo { CountKill = true, CountItem = false, Friendly = true }, all),
				new TallyThing(9, new ThingTallyInfo { CountKill = false }, all),
				new TallyThing(10, null, all),
			};

			TallyColumn col = ThingTally.Compute(things, new[] { 3 }, TallyGameMode.Single, 0)[0];

			Assert.Equal(new[] { 0 }, col.KillThings);
			Assert.Equal(60L, col.MonsterHealth);
			Assert.Equal(new[] { 5 }, col.ItemThings);
			Assert.Equal(new[] { 7 }, col.SecretThingList);
			Assert.Equal(new[] { 9, 10 }, col.UnresolvedThings);
		}

		[Fact]
		public void AmountsSumPerSkill()
		{
			var easy = ThingSpawnFilter.UdmfView(false, Bits(1), Doom);
			var hard = ThingSpawnFilter.UdmfView(false, Bits(4), Doom);
			var clipbox = new ThingTallyInfo { CountKill = false, CountItem = false, HealthAmount = 0, ArmorAmount = 0 };
			clipbox.AddAmmo("Clip", 50);
			var soulsphere = new ThingTallyInfo { CountKill = false, CountItem = true, HealthAmount = 100, ArmorAmount = 0 };

			var cols = ThingTally.Compute(new[] { new TallyThing(0, clipbox, easy), new TallyThing(1, soulsphere, hard), new TallyThing(2, clipbox, hard) },
				Skills, TallyGameMode.Single, 0);

			Assert.Equal(new long[] { 50, 50, 0, 50, 50 }, cols.Select(c => c.Ammo.TryGetValue("clip", out long a) ? a : 0));
			Assert.Equal(new long[] { 0, 0, 0, 100, 100 }, cols.Select(c => c.Health));
			Assert.Equal(new[] { 0, 0, 0, 1, 1 }, cols.Select(c => c.Items));
		}

		[Fact]
		public void SkillsSortByIndex()
		{
			var skills = new List<SkillInfo> { new SkillInfo(3, "c"), new SkillInfo(1, "a"), new SkillInfo(5, "e"), new SkillInfo(2, "b") };
			Assert.Equal(new[] { 1, 2, 3, 5 }, ThingSpawnFilter.SortedSkills(skills).Select(s => s.Index));
		}
	}
}
