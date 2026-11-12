using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class TextureHistoryTests
	{
		[Fact]
		public void ConfigurationDefaultsToTheCap()
		{
			Assert.Equal(TextureHistory.MAX_CAPACITY, new ProgramConfiguration().TextureHistorySize);
		}

		[Theory]
		[InlineData(-1, 0)]
		[InlineData(0, 0)]
		[InlineData(TextureHistory.MAX_CAPACITY - 1, TextureHistory.MAX_CAPACITY - 1)]
		[InlineData(TextureHistory.MAX_CAPACITY + 1, TextureHistory.MAX_CAPACITY)]
		[InlineData(64, TextureHistory.MAX_CAPACITY)]
		public void ConfigurationLoadsAndClampsHistorySize(int value, int expected)
		{
			string path = Path.Combine(Path.GetTempPath(), "texture-history-" + Guid.NewGuid().ToString("N") + ".cfg");
			try
			{
				File.WriteAllText(path, "currentversion = 1;\ntexturehistorysize = " + value + ";\n");
				ProgramConfiguration config = new ProgramConfiguration();
				Assert.True(config.Load(path, path, path));
				Assert.Equal(expected, config.TextureHistorySize);
			}
			finally { File.Delete(path); }
		}

		[Fact]
		public void ConfigurationImmediatelyPrunesLiveHistory()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				TextureHistory history = new TextureHistory(3, false);
				Set(General.Map, "texturehistory", history);
				history.Record("ONE", TextureHistoryKind.Texture);
				history.Record("TWO", TextureHistoryKind.Texture);
				ProgramConfiguration config = new ProgramConfiguration();
				config.TextureHistorySize = 1;
				Assert.Equal("TWO", Assert.Single(history.Get(TextureHistoryKind.Texture)));
				config.TextureHistorySize = 0;
				Assert.Empty(history.Get(TextureHistoryKind.Texture));
			}
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void RecordingValidatesNamesInTheSessionNamespace(bool mixed)
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				TextureHistory history = new TextureHistory(3, mixed);
				Set(General.Map, "texturehistory", history);
				var data = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
				GC.SuppressFinalize(data);
				Set(data, "textures", new Dictionary<long, ImageData> { { Lump.MakeLongName("WALL"), null } });
				Set(data, "flats", new Dictionary<long, ImageData> { { Lump.MakeLongName("FLOOR"), null } });
				Set(data, "texturenamesshorttofull", new Dictionary<long, long>());
				Set(data, "flatnamesshorttofull", new Dictionary<long, long>());
				Set(General.Map, "data", data);
				foreach(string invalid in new[] { null, "", " ", "-", "UNKNOWN" }) General.Map.RecordTextureUse(invalid, false);
				Assert.Empty(history.Get(TextureHistoryKind.Texture));
				General.Map.RecordTextureUse("FLOOR", false);
				Assert.Equal(mixed ? 1 : 0, history.Get(TextureHistoryKind.Texture).Count);
				General.Map.RecordTextureUse("WALL", false);
				General.Map.RecordTextureUse("FLOOR", true);
				Assert.Equal(mixed ? 2 : 1, history.Get(TextureHistoryKind.Texture).Count);
				Assert.Equal("FLOOR", history.Get(TextureHistoryKind.Flat)[0]);
				Set(General.Map, "isdisposed", true);
				General.Map.RecordTextureUse("WALL", false);
				Assert.Equal(mixed ? "FLOOR" : "WALL", history.Get(TextureHistoryKind.Texture)[0]);
			}
		}

		private static void Set(object target, string name, object value)
		{
			target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
		}

		[Fact]
		public void RecordOrdersNewestFirstAndPromotesDuplicates()
		{
			TextureHistory history = new TextureHistory(3, false);
			history.Record("ONE", TextureHistoryKind.Texture);
			history.Record("TWO", TextureHistoryKind.Texture);
			history.Record("THREE", TextureHistoryKind.Texture);
			Assert.Equal(new[] { "THREE", "TWO", "ONE" }, history.Get(TextureHistoryKind.Texture));
			Assert.True(history.Record("one", TextureHistoryKind.Texture));
			Assert.Equal(new[] { "one", "THREE", "TWO" }, history.Get(TextureHistoryKind.Texture));
			Assert.True(history.Record("One", TextureHistoryKind.Texture));
			Assert.Equal("One", history.Get(TextureHistoryKind.Texture)[0]);
			Assert.False(history.Record("One", TextureHistoryKind.Texture));
		}

		[Fact]
		public void TouchKeepsExistingSlotAndSpellingWithoutNotification()
		{
			TextureHistory history = new TextureHistory(3, false);
			history.Record("ONE", TextureHistoryKind.Texture);
			history.Record("TWO", TextureHistoryKind.Texture);
			int changes = 0;
			history.Changed += (sender, e) => changes++;
			Assert.False(history.Touch("one", TextureHistoryKind.Texture));
			Assert.Equal(new[] { "TWO", "ONE" }, history.Get(TextureHistoryKind.Texture));
			Assert.Equal(0, changes);
			Assert.True(history.Touch("THREE", TextureHistoryKind.Texture));
			Assert.Equal(new[] { "THREE", "TWO", "ONE" }, history.Get(TextureHistoryKind.Texture));
			Assert.Equal(1, changes);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData(" ")]
		[InlineData("-")]
		public void InvalidNamesDoNotRecordOrTouch(string name)
		{
			TextureHistory history = new TextureHistory(3, false);
			Assert.False(history.Record(name, TextureHistoryKind.Texture));
			Assert.False(history.Touch(name, TextureHistoryKind.Flat));
			Assert.Empty(history.Get(TextureHistoryKind.Texture));
			Assert.Empty(history.Get(TextureHistoryKind.Flat));
		}

		[Theory]
		[InlineData(-1, 0)]
		[InlineData(0, 0)]
		[InlineData(TextureHistory.MAX_CAPACITY - 1, TextureHistory.MAX_CAPACITY - 1)]
		[InlineData(TextureHistory.MAX_CAPACITY + 1, TextureHistory.MAX_CAPACITY)]
		public void CapacityClamps(int requested, int expected)
		{
			TextureHistory history = new TextureHistory(requested, false);
			Assert.Equal(expected, history.Capacity);
			history.Capacity = requested;
			Assert.Equal(expected, history.Capacity);
		}

		[Fact]
		public void CapacityPrunesBothNamespacesAndZeroClearsAndDisables()
		{
			TextureHistory history = new TextureHistory(2, false);
			foreach(TextureHistoryKind kind in new[] { TextureHistoryKind.Texture, TextureHistoryKind.Flat })
			{
				history.Record("ONE", kind);
				history.Record("TWO", kind);
				history.Record("THREE", kind);
				Assert.Equal(new[] { "THREE", "TWO" }, history.Get(kind));
			}
			history.Capacity = 1;
			Assert.Equal("THREE", Assert.Single(history.Get(TextureHistoryKind.Texture)));
			Assert.Equal("THREE", Assert.Single(history.Get(TextureHistoryKind.Flat)));
			history.Capacity = 0;
			Assert.Empty(history.Get(TextureHistoryKind.Texture));
			Assert.Empty(history.Get(TextureHistoryKind.Flat));
			Assert.False(history.Record("FOUR", TextureHistoryKind.Texture));
			Assert.False(history.Touch("FOUR", TextureHistoryKind.Flat));
		}

		[Fact]
		public void SeparateNamespacesDoNotDeduplicateEachOther()
		{
			TextureHistory history = new TextureHistory(2, false);
			history.Record("ONE", TextureHistoryKind.Texture);
			history.Record("one", TextureHistoryKind.Flat);
			history.Remove("ONE", TextureHistoryKind.Flat);
			Assert.Equal("ONE", Assert.Single(history.Get(TextureHistoryKind.Texture)));
			Assert.Empty(history.Get(TextureHistoryKind.Flat));
			Assert.False(history.Mixed);
		}

		[Fact]
		public void MixedNamespacesShareOneListAndCapacity()
		{
			TextureHistory history = new TextureHistory(2, true);
			history.Record("ONE", TextureHistoryKind.Texture);
			history.Record("TWO", TextureHistoryKind.Flat);
			Assert.Same(history.Get(TextureHistoryKind.Texture), history.Get(TextureHistoryKind.Flat));
			Assert.False(history.Touch("one", TextureHistoryKind.Flat));
			history.Record("one", TextureHistoryKind.Flat);
			history.Record("THREE", TextureHistoryKind.Texture);
			Assert.Equal(new[] { "THREE", "one" }, history.Get(TextureHistoryKind.Flat));
			history.Remove("ONE", TextureHistoryKind.Texture);
			Assert.Equal("THREE", Assert.Single(history.Get(TextureHistoryKind.Flat)));
			Assert.True(history.Mixed);
		}

		[Fact]
		public void ChangesNotifyOnceIncludingCapacityAndNoOpsStaySilent()
		{
			TextureHistory history = new TextureHistory(3, false);
			int changes = 0;
			history.Changed += (sender, e) => changes++;
			history.Record("ONE", TextureHistoryKind.Texture);
			Assert.Equal(1, changes);
			history.Record("ONE", TextureHistoryKind.Texture);
			history.Touch("one", TextureHistoryKind.Texture);
			history.Remove("missing", TextureHistoryKind.Flat);
			history.Capacity = 3;
			Assert.Equal(1, changes);
			history.Record("TWO", TextureHistoryKind.Flat);
			history.Record("THREE", TextureHistoryKind.Texture);
			history.Capacity = 1;
			Assert.Equal(4, changes);
			history.Remove("three", TextureHistoryKind.Texture);
			Assert.Equal(5, changes);
			history.Clear();
			Assert.Equal(6, changes);
			history.Clear();
			Assert.Equal(6, changes);
		}
	}
}
