using System.Collections.Generic;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Macros;
using Xunit;

namespace Builder.Tests
{
	// Macros live in UDBuilder.cfg: a codec or save slip loses the user's macros
	public class MacroStoreTests
	{
		private static Macro Make(string name, string steps, int slot = 0)
		{
			List<MacroStep> s;
			Assert.True(MacroStore.TryDecodeSteps(steps, out s), steps);
			return new Macro { Name = name, Mode = "SectorsMode", Slot = slot, Steps = s };
		}

		// Through the text form, as a restart does
		private static MacroStore Reload(MacroStore store)
		{
			Configuration cfg = new Configuration(true);
			cfg.WriteSetting("other", 5);
			store.Save(cfg);
			Configuration back = new Configuration(true);
			Assert.True(back.InputConfiguration(cfg.OutputConfiguration()));
			Assert.Equal(5, back.ReadSetting("other", 0));
			MacroStore loaded = new MacroStore();
			loaded.Load(back);
			return loaded;
		}

		[Fact]
		public void StepCodecRoundTripsAndRejectsMalformed()
		{
			List<MacroStep> s;
			Assert.True(MacroStore.TryDecodeSteps("buildermodes_fliplinedefs,buildermodes_lowerfloor8*3", out s));
			Assert.Equal(2, s.Count);
			Assert.Equal(3, s[1].Count);
			Assert.Equal("buildermodes_fliplinedefs,buildermodes_lowerfloor8*3", MacroStore.EncodeSteps(s));

			foreach(string bad in new[] { "", "a*0", "a*-1", "a*x", "a**2", "a,,b", "A", "a*10000", "a b" })
				Assert.False(MacroStore.TryDecodeSteps(bad, out s), bad);
		}

		[Fact]
		public void AppendCollapsesRepeats()
		{
			List<MacroStep> s = new List<MacroStep>();
			foreach(string a in new[] { "a", "a", "a", "b", "a" }) MacroStore.AppendStep(s, a);
			Assert.Equal("a*3,b,a", MacroStore.EncodeSteps(s));
		}

		[Fact]
		public void SaveAndLoadKeepOrderPastNineAndMalformedSteps()
		{
			MacroStore store = new MacroStore();
			for(int i = 1; i <= 12; i++) Assert.True(store.Put(Make("M" + i, "a*" + i), null));
			store.Macros[4].MalformedSteps = "broken**";
			store.Macros[0].AnyMode = true;

			MacroStore loaded = Reload(store);
			Assert.Equal(12, loaded.Macros.Count);
			for(int i = 0; i < 12; i++) Assert.Equal("M" + (i + 1), loaded.Macros[i].Name);
			Assert.Equal("broken**", loaded.Macros[4].MalformedSteps);
			Assert.False(loaded.Macros[4].IsAvailable(n => true));
			Assert.Equal(12, loaded.Macros[11].StepCount);
			Assert.True(loaded.Macros[0].AnyMode);
			Assert.Equal("SectorsMode", loaded.Macros[1].Mode);
		}

		[Fact]
		public void UnknownActionsAreKeptButUnavailable()
		{
			MacroStore store = new MacroStore();
			store.Put(Make("X", "known,gone*2"), null);
			MacroStore loaded = Reload(store);
			Macro m = loaded.Macros[0];
			Assert.Equal("known,gone*2", MacroStore.EncodeSteps(m.Steps));
			Assert.False(m.IsAvailable(n => n == "known"));
			Assert.Equal(new List<string> { "gone" }, m.MissingActions(n => n == "known"));
		}

		[Fact]
		public void SlotsMoveAndNamesStayUnique()
		{
			MacroStore store = new MacroStore();
			Macro a = Make("A", "x", 3);
			Macro b = Make("B", "y", 3);
			Assert.True(store.Put(a, null));
			Assert.True(store.Put(b, null));
			Assert.Equal(0, a.Slot);
			Assert.Same(b, store.BySlot(3));

			Assert.False(store.Put(Make(" a ", "z"), null));
			Assert.True(store.Put(Make("a", "z"), a)); // a rename to its own name, case changed
			Assert.Equal(2, store.Macros.Count);
			Assert.Equal("A (2)", store.UniqueName("A"));
		}

		[Fact]
		public void DeletedMacroStaysDeleted()
		{
			MacroStore store = new MacroStore();
			store.Put(Make("A", "x"), null);
			store.Put(Make("B", "y"), null);
			MacroStore first = Reload(store);
			first.Remove(first.ByName("A"));
			MacroStore second = Reload(first);
			Assert.Equal(1, second.Macros.Count);
			Assert.Null(second.ByName("A"));

			second.Remove(second.ByName("B"));
			Assert.Equal(0, Reload(second).Macros.Count);
		}
	}
}
