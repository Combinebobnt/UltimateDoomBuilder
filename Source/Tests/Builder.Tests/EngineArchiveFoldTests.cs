using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Data.Scripting;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.ZDoom;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	// Shares General.map with SndInfoParserTests, so xunit must not run the two in parallel
	[Collection("GeneralStatics")]
	public class EngineArchiveFoldTests
	{
		// No icon, so the fold tests don't load Builder.exe's image resources
		private sealed class Item : ErrorItem
		{
			public Item(string description) : base(ErrorType.Warning, description) { }
			protected override Image? GetIcon() { return null; }
		}

		private static RequiredArchive Zandronum()
		{
			var entries = new List<RequiredArchiveEntry> { new RequiredArchiveEntry("actor", null), new RequiredArchiveEntry(null, "gamemode.txt") };
			return new RequiredArchive("zandronum", "zandronum.pk3", true, entries);
		}

		private static EngineNames Names() { return new EngineNames(new[] { "ammo", "clip" }, new[] { "healthpotion" }); }

		// DecorateActorStructure only checks a parent while a map is open, and then reads the config's thing types and the format's number range
		private static void OpenFakeMap()
		{
			var config = (GameConfiguration)RuntimeHelpers.GetUninitializedObject(typeof(GameConfiguration));
			typeof(GameConfiguration).GetField("things", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, new Dictionary<int, ThingTypeInfo>());
			typeof(GameConfiguration).GetField("decorategames", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, "doom");

			var map = (MapManager)RuntimeHelpers.GetUninitializedObject(typeof(MapManager));
			typeof(MapManager).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(map, config);
			object io = RuntimeHelpers.GetUninitializedObject(typeof(UniversalMapSetIO));
			typeof(MapManager).GetField("io", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(map, io);

			// Their finalizers would dispose fields the real constructors set
			GC.SuppressFinalize(config);
			GC.SuppressFinalize(map);
			GC.SuppressFinalize(io);
			typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, map);
		}

		private static void CloseFakeMap()
		{
			typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
		}

		[Fact]
		public void DecorateParser_HandsTheSinkEachMissingParentAndReplacedClass()
		{
			var names = new List<string>();
			var parser = new DecorateParser(new Dictionary<string, ActorStructure>()) { UnresolvedClassSink = (name, item) => { names.Add(name); return true; } };

			var resource = (ScriptResource)RuntimeHelpers.GetUninitializedObject(typeof(ScriptResource));
			typeof(ScriptResource).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(resource, new HashSet<string>());
			parser.ScriptResources[string.Empty] = resource;

			string source = "actor A : Ammo 20001 {}\nactor B : MyTypo 20002 {}\nactor C replaces Clip {}\n";
			var stream = new MemoryStream(source.Select(c => (byte)c).ToArray());
			var data = new TextResourceData(stream, new DataLocation(DataLocation.RESOURCE_DIRECTORY, "test", false, false, false, new List<string>()), "DECORATE");

			OpenFakeMap();
			try
			{
				Assert.True(parser.Parse(data, true), parser.ErrorDescription);
			}
			finally
			{
				CloseFakeMap();
			}

			Assert.Equal("Ammo,MyTypo,Clip", string.Join(",", names));
		}

		[Fact]
		public void Fold_WithTheArchiveMissing_CountsEngineNamesAndKeepsTheRest()
		{
			var output = new List<ErrorItem>();
			var fold = new EngineArchiveFold(output.Add);
			Assert.True(fold.OnUnresolvedClass("Ammo", new Item("ammo")));
			Assert.True(fold.OnUnresolvedClass("MyTypo", new Item("typo")));
			Assert.True(fold.OnUnresolvedClass("Clip", new Item("clip")));
			Assert.Empty(output);

			fold.Decide(new[] { Zandronum() }, "Death and Decay", Names);
			// Light errors arrive after the decision
			Assert.True(fold.Route(EngineNameKind.Light, "HEALTHPOTION", new Item("light")));
			Assert.False(fold.Route(EngineNameKind.Light, "DNDTORCH", new Item("modlight")));
			fold.Emit();

			Assert.Equal(3, output.Count);
			Assert.Equal("typo", output[0].Description);
			Assert.Contains("\"Death and Decay\" has no \"zandronum.pk3\"", output[1].Description);
			Assert.Contains("\"Exclude this resource from testing parameters\" checked", output[1].Description);
			Assert.Equal("Folded 3 messages about engine classes and lights while \"zandronum.pk3\" is missing (most frequent: Ammo 1, Clip 1, HEALTHPOTION 1).", output[2].Description);
		}

		[Fact]
		public void Fold_WithNothingMissing_FlushesInOrderWithoutLoadingNames()
		{
			var output = new List<ErrorItem>();
			var fold = new EngineArchiveFold(output.Add);
			fold.OnUnresolvedClass("Ammo", new Item("ammo"));
			fold.OnUnresolvedClass("MyTypo", new Item("typo"));
			fold.OnUnresolvedClass("Clip", new Item("clip"));

			int loads = 0;
			fold.Decide(new RequiredArchive[0], "Death and Decay", () => { loads++; return Names(); });
			Assert.False(fold.OnUnresolvedClass("Ammo", new Item("late")));
			fold.Emit();

			Assert.Equal(0, loads);
			Assert.Equal("ammo,typo,clip", string.Join(",", output.Select(i => i.Description)));
		}

		[Fact]
		public void FindMissing_NeedsEveryClassAndLumpEntryAndIgnoresTheNeedExcludeEntry()
		{
			// GameConfiguration turns need_exclude into a (null, null) entry
			var entries = new List<RequiredArchiveEntry> { new RequiredArchiveEntry("actor", null), new RequiredArchiveEntry(null, "x11r6rgb.txt"), new RequiredArchiveEntry(null, null) };
			var archives = new[] { new RequiredArchive("gzdoom", "gzdoom.pk3", true, entries) };

			Assert.Empty(EngineArchiveFold.FindMissing(archives, c => c == "actor", l => l == "x11r6rgb.txt"));
			Assert.Single(EngineArchiveFold.FindMissing(archives, c => false, l => l == "x11r6rgb.txt"));
			Assert.Single(EngineArchiveFold.FindMissing(archives, c => c == "actor", l => false));
		}

		[Fact]
		public void EngineNames_ShippedListHoldsZandronumClassesAndDoomdefsLights()
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location)!, "Configurations", "Includes", "Engine_names.cfg");
			EngineNames? names = EngineNames.Load(path, out string error);
			Assert.True(names != null, error);

			foreach(string cls in new[] { "actor", "ammo", "custominventory", "powerdamage", "RedCard" })
				Assert.True(names!.Contains(EngineNameKind.Class, cls), "class " + cls + " missing");
			// zandronum.pk3 doomdefs.txt; DnD's GLDEFS objects point at it
			Assert.True(names!.Contains(EngineNameKind.Light, "HEALTHPOTION"), "light healthpotion missing");
			Assert.False(names.Contains(EngineNameKind.Class, "healthpotion"), "a light name leaked into the classes");
		}
	}
}
