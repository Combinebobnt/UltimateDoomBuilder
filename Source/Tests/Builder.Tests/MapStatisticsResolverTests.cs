using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Data.Scripting;
using CodeImp.DoomBuilder.Dehacked;
using CodeImp.DoomBuilder.Map.Statistics;
using CodeImp.DoomBuilder.ZDoom;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	public class MapStatisticsResolverTests
	{
		private static ActorLink Link(string name, string flags = "", params (string Key, string Value)[] props)
		{
			var f = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach(string token in flags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
				f[token.Substring(1)] = token[0] == '+';
			var p = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach(var prop in props) p[prop.Key] = new List<string> { prop.Value };
			return new ActorLink(name, f, p);
		}

		private static readonly ActorLink Root = Link("Actor", "", ("health", "DEFAULT_HEALTH"));

		[Fact]
		public void CountKillFlagOverridesInheritedMonster()
		{
			// DECORATE's monster keyword arrives as flags, ZScript's as a value-less property
			var decorate = Link("Base", "+countkill +ismonster", ("health", "60"));
			var zscript = Link("ZBase", "", ("monster", ""), ("health", "60"));

			Assert.True(ThingTallyResolver.WalkChain(new[] { Link("Plain"), zscript, Root }, true, null).CountKill == true);
			Assert.True(ThingTallyResolver.WalkChain(new[] { Link("Calm", "-countkill"), decorate, Root }, true, null).CountKill == false);
			Assert.True(ThingTallyResolver.WalkChain(new[] { Link("Calm", "-countkill"), zscript, Root }, true, null).CountKill == false);
			Assert.True(ThingTallyResolver.WalkChain(new[] { Link("Angry", "+countkill"), Link("Prop"), Root }, true, null).CountKill == true);
		}

		[Fact]
		public void HealthReadsTheFirstClassThatSetsIt()
		{
			Assert.Equal(1000, ThingTallyResolver.WalkChain(new[] { Link("Thing"), Root }, true, null).SpawnHealth);
			Assert.Equal(1000, ThingTallyResolver.WalkChain(new[] { Link("Decorate") }, true, null).SpawnHealth);
			Assert.Equal(25, ThingTallyResolver.WalkChain(new[] { Link("Weak", "", ("health", "25")), Root }, true, null).SpawnHealth);

			// An expression stays unresolved instead of falling back to a parent's value
			var expr = ThingTallyResolver.WalkChain(new[] { Link("Expr", "", ("health", "20 * 3")), Link("Mid", "", ("health", "50")), Root }, true, null);
			Assert.Null(expr.SpawnHealth);
			Assert.False(expr.IsUnresolved);
		}

		[Fact]
		public void BrokenChainLeavesUnsetFieldsUnresolved()
		{
			var broken = ThingTallyResolver.WalkChain(new[] { Link("Orphan", "+countitem") }, false, null);
			Assert.True(broken.CountItem == true);
			Assert.Null(broken.CountKill);
			Assert.Null(broken.SpawnHealth);
			Assert.Null(broken.HealthAmount);
			Assert.True(broken.IsUnresolved);
		}

		[Fact]
		public void RandomSpawnerIsUnresolved()
		{
			var spawner = ThingTallyResolver.WalkChain(new[] { Link("MySpawner", "+countkill"), Link("RandomSpawner"), Root }, true, null);
			Assert.True(spawner.IsUnresolved);
			Assert.Null(spawner.CountKill);
		}

		[Fact]
		public void PickupAmountsByBaseClass()
		{
			var inventory = Link("Inventory", "", ("inventory.amount", "1"));
			var clip = Link("Clip", "", ("inventory.amount", "10"));
			var ammo = Link("Ammo");

			var clipbox = ThingTallyResolver.WalkChain(new[] { Link("ClipBox", "", ("inventory.amount", "50")), clip, ammo, inventory, Root }, true, null);
			Assert.Equal(new[] { "Clip" }, clipbox.Ammo!.Keys);
			Assert.Equal(50, clipbox.Ammo["Clip"]);

			var stim = ThingTallyResolver.WalkChain(new[] { Link("Stimpack", "", ("inventory.amount", "10")), Link("Health"), inventory, Root }, true, null);
			Assert.Equal(10, stim.HealthAmount);

			var armor = ThingTallyResolver.WalkChain(new[] { Link("GreenArmor", "", ("armor.saveamount", "100")), Link("BasicArmorPickup"), inventory, Root }, true, null);
			Assert.Equal(100, armor.ArmorAmount);
			Assert.Equal(0, armor.HealthAmount);

			var shotgun = ThingTallyResolver.WalkChain(new[] { Link("Shotgun", "", ("weapon.ammotype1", "\"Shell\""), ("weapon.ammogive1", "8")), Link("Weapon"), inventory, Root },
				true, s => s == "Shell" ? "ShellGroup" : s);
			Assert.Equal(8, shotgun.Ammo!["ShellGroup"]);

			// CustomInventory is left for the vanilla table; non-inventory gets nothing
			Assert.Null(ThingTallyResolver.WalkChain(new[] { Link("Megasphere"), Link("CustomInventory"), inventory, Root }, true, null).HealthAmount);
			Assert.Equal(0, ThingTallyResolver.WalkChain(new[] { Link("Imp"), Root }, true, null).HealthAmount);
		}

		private static readonly Dictionary<long, string> Mnemonics = new Dictionary<long, string>
		{
			{ 2, "solid" }, { 4, "shootable" }, { 4194304, "countkill" }, { 8388608, "countitem" }
		};

		private static DehackedThing Base() => new DehackedThing(12, "Imp", new Dictionary<string, string> { { "bits", "4194310" }, { "id #", "3001" } });

		private static DehackedThing Patch(Dictionary<string, string> props)
		{
			var patch = new DehackedThing(12, "Imp", props);
			patch.Process(new Dictionary<int, DehackedFrame>(), Mnemonics, Base(), new HashSet<string>());
			return patch;
		}

		[Fact]
		public void DehackedPatchSetsOnlyItsOwnFields()
		{
			// Hit points only: bits were copied from the base thing, so they stay for later routes
			var hp = ThingTallyResolver.FromPatch(Patch(new Dictionary<string, string> { { "Hit points", "100" } }), Base());
			Assert.Equal(100, hp.SpawnHealth);
			Assert.Null(hp.CountKill);

			var bits = ThingTallyResolver.FromPatch(Patch(new Dictionary<string, string> { { "Bits", "SOLID+SHOOTABLE+COUNTITEM" } }), Base());
			Assert.True(bits.CountKill == false);
			Assert.True(bits.CountItem == true);
			Assert.Null(bits.SpawnHealth);
		}

		[Fact]
		public void ConfigBaseBitsDecodeFromProps()
		{
			var info = ThingTallyResolver.FromConfigBase(Base(), Mnemonics);
			Assert.True(info.CountKill == true);
			Assert.True(info.CountItem == false);
		}

		[Fact]
		public void VanillaTableMatchesInfo()
		{
			var imp = VanillaDoomAmounts.Get(3001)!;
			Assert.True(imp.CountKill == true);
			Assert.Equal(60, imp.SpawnHealth);
			Assert.True(VanillaDoomAmounts.Get(3006)!.CountKill == false);
			Assert.Equal(200, VanillaDoomAmounts.Get(83)!.ArmorAmount);
			Assert.Equal(4, VanillaDoomAmounts.Get(8)!.Ammo!.Count);
			Assert.Null(VanillaDoomAmounts.Get(31337));
		}

		private static DecorateParser Parse(string source)
		{
			var parser = new DecorateParser(new Dictionary<string, ActorStructure>()) { NoWarnings = true };
			var resource = (ScriptResource)RuntimeHelpers.GetUninitializedObject(typeof(ScriptResource));
			typeof(ScriptResource).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(resource, new HashSet<string>());
			parser.ScriptResources[string.Empty] = resource;

			var stream = new MemoryStream(source.Select(c => (byte)c).ToArray());
			var data = new TextResourceData(stream, new DataLocation(DataLocation.RESOURCE_DIRECTORY, "test", false, false, false, new List<string>()), "DECORATE");
			Assert.True(parser.Parse(data, true), parser.ErrorDescription);
			return parser;
		}

		[Fact]
		public void ParsedDecorateChain()
		{
			var parser = Parse("actor Base { Health 77\n Monster\n }\n"
				+ "actor Calm : Base { -COUNTKILL\n }\n"
				+ "actor Orphan : NeverParsed { +COUNTITEM\n }\n");

			var calm = ThingTallyResolver.BuildChain(parser.AllActorsByClass["calm"], out bool complete);
			var info = ThingTallyResolver.WalkChain(calm, complete, null);
			Assert.True(complete);
			Assert.True(info.CountKill == false);
			Assert.Equal(77, info.SpawnHealth);

			ThingTallyResolver.BuildChain(parser.AllActorsByClass["orphan"], out bool orphancomplete);
			Assert.False(orphancomplete);
		}
	}
}
