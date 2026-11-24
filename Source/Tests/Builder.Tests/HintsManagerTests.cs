using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder.IO;
using Xunit;
using UdbAction = CodeImp.DoomBuilder.Actions.Action;

namespace Builder.Tests
{
	public class HintsManagerTests
	{
		private const int X = (int)Keys.X;
		private const int Y = (int)Keys.Y;
		private const int Ctrl = (int)Keys.Control;
		private const int Shift = (int)Keys.Shift;
		private const int Alt = (int)Keys.Alt;

		public class BaseHintMode { public void Run() { } }
		public class DerivedHintMode : BaseHintMode { }
		public class OtherHintMode { }

		private static readonly string Prefix = typeof(HintsManagerTests).Assembly.GetName().Name.ToLowerInvariant() + "_";

		private static UdbAction MakeAction(string name, int primary, int alternate = 0, string options = "", string title = null)
		{
			Configuration cfg = new Configuration();
			cfg.InputConfiguration("a { " + (title != null ? "title = \"" + title + "\"; " : "") + options + " }");
			Assert.False(cfg.ErrorResult);
			return new UdbAction(cfg, name, "a", primary, alternate);
		}

		private static UdbAction Bound(UdbAction a, object target = null)
		{
			BaseHintMode t = (target as BaseHintMode) ?? new BaseHintMode();
			a.BindBegin(t.Run);
			return a;
		}

		private class Harness
		{
			public readonly List<string> Shown = new List<string>();
			public readonly Dictionary<string, UdbAction> Actions = new Dictionary<string, UdbAction>();
			public readonly HintsManager Manager;

			public Harness(string cfg)
			{
				Manager = new HintsManager(Shown.Add, n => Actions.ContainsKey(n) ? Actions[n] : null, () => Actions.Values, () => null);
				Manager.AddHints(HintsManager.ParseHints(new StringReader(cfg), Prefix));
			}

			public UdbAction Add(UdbAction a) { Actions[a.Name] = a; return a; }
			public string Last { get { return Shown[Shown.Count - 1]; } }
		}

		private const string Cfg =
			"// comment\n" +
			"class BaseHintMode\n" +
			"group general\n" +
			"\"Base <b>bold</b><br>next\"\n" +
			"group mod_ctrl\n" +
			"\"Ctrl does a base thing\"\n" +
			"\n" +
			"class DerivedHintMode\n" +
			"group general\n" +
			"\"Press <k>t_go</k> to go\"\n" +
			"\"Press <k>t_missing</k> {here}\"\n";

		[Fact]
		public void ParseKeepsClassesGroupsAndRawLines()
		{
			var parsed = HintsManager.ParseHints(new StringReader(Cfg), "p_");
			Assert.Equal(2, parsed.Count);
			Assert.Equal("Base <b>bold</b><br>next", parsed["p_BaseHintMode"]["general"][0]);
			Assert.Equal("Ctrl does a base thing", parsed["p_BaseHintMode"]["mod_ctrl"][0]);
			Assert.Equal(2, parsed["p_DerivedHintMode"]["general"].Count);
		}

		[Fact]
		public void MalformedFilesFailWithAMessage()
		{
			Assert.Throws<InvalidDataException>(() => HintsManager.ParseHints(new StringReader("class A\ngroup g\nclass A\ngroup g\n"), "p_"));
			Assert.Throws<InvalidDataException>(() => HintsManager.ParseHints(new StringReader("class A\ngroup g\ngroup g\n"), "p_"));
			Assert.Throws<InvalidDataException>(() => HintsManager.ParseHints(new StringReader("class A\n\"line\"\n"), "p_"));
			Assert.Throws<InvalidDataException>(() => HintsManager.ParseHints(new StringReader("group g\n"), "p_"));
		}

		[Fact]
		public void TagsRenderToRtfAndKeysResolveAtShowTime()
		{
			Harness h = new Harness(Cfg);
			UdbAction go = h.Add(MakeAction("t_go", X));

			h.Manager.ShowHints(typeof(BaseHintMode), HintsManager.GENERAL);
			Assert.Equal("{\\rtf1 Base {\\b bold}\\par next}", h.Last);

			h.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.GENERAL);
			Assert.Contains("{\\b " + UdbAction.GetShortcutKeyDesc(X) + "}", h.Last);

			go.SetShortcutKeys(Ctrl | Y, 0);
			h.Manager.Refresh();
			Assert.Contains("{\\b " + UdbAction.GetShortcutKeyDesc(Ctrl | Y) + "}", h.Last);
		}

		[Fact]
		public void UnknownActionShowsItsNameAndTextIsEscaped()
		{
			Harness h = new Harness(Cfg);
			h.Add(MakeAction("t_go", X));
			h.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.GENERAL);
			Assert.Contains("{\\b t_missing} \\{here\\}", h.Last);
		}

		[Fact]
		public void KeyTextListsAlternateAndTreatsUnsetAsUnbound()
		{
			Assert.Equal(UdbAction.GetShortcutKeyDesc(X) + " or " + UdbAction.GetShortcutKeyDesc(Ctrl | Y),
				HintsManager.DescribeKeys(MakeAction("t", X, Ctrl | Y)));
			Assert.Equal("Thing (not bound to a key)", HintsManager.DescribeKeys(MakeAction("t", 0, 0, "", "Thing")));
			Assert.Equal("Thing (not bound to a key)", HintsManager.DescribeKeys(MakeAction("t", -1, 0, "", "Thing")));
		}

		[Fact]
		public void RtfSpecialsInKeyTextAreEscaped()
		{
			string backslash = UdbAction.GetShortcutKeyDesc((int)Keys.Oem5);
			Assert.Equal("\\", backslash);
			Assert.Equal("{\\b \\\\}", HintsManager.RenderLine("<k>k</k>", n => backslash));
			Assert.Equal("caf\\u233?", HintsManager.EscapeRtf("caf" + (char)233));
		}

		[Fact]
		public void MissingGroupFallsBackToBaseTypeThenDefault()
		{
			Harness h = new Harness(Cfg);
			h.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.MULTISELECTION);
			Assert.Contains("F1", h.Last);
			h.Manager.ShowHints(typeof(OtherHintMode), HintsManager.GENERAL);
			Assert.Contains("F1", h.Last);

			Harness g = new Harness("class BaseHintMode\ngroup multiselection\n\"from base\"\n");
			g.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.MULTISELECTION);
			Assert.Equal("{\\rtf1 from base}", g.Last);
		}

		[Fact]
		public void ShortcutListMatchesExactModifiersInEitherSlot()
		{
			var actions = new List<UdbAction>
			{
				Bound(MakeAction("ctrlx", Ctrl | X, 0, "", "Ctrl X")),
				Bound(MakeAction("altslot", X, Ctrl | Y, "", "Alt slot")),
				Bound(MakeAction("disregard", X, 0, "disregardcontrol = true;", "Disregard")),
				Bound(MakeAction("ctrlshift", Ctrl | Shift | X, 0, "", "Ctrl Shift X")),
				Bound(MakeAction("unset", -1, 0, "", "Unset")),
				Bound(MakeAction("puremod", (int)Keys.ControlKey | Ctrl, 0, "", "Pure")),
				MakeAction("unbound", Ctrl | Y, 0, "", "Not bound"),
				Bound(MakeAction("wheel", (int)SpecialKeys.MScrollUp | Ctrl, 0, "allowscroll = true;", "Wheel")),
			};

			var ctrl = HintsManager.BuildShortcutList(actions, Ctrl, null).Select(p => p.Value).ToList();
			Assert.Equal(3, ctrl.Count);
			Assert.Contains("Ctrl X", ctrl);
			Assert.Contains("Alt slot", ctrl);
			Assert.Contains("Wheel", ctrl);

			var altslot = HintsManager.BuildShortcutList(actions, Ctrl, null).Single(p => p.Value == "Alt slot");
			Assert.Equal(UdbAction.GetShortcutKeyDesc(Ctrl | Y), altslot.Key);

			var both = HintsManager.BuildShortcutList(actions, Ctrl | Shift, null).Select(p => p.Value).ToList();
			Assert.Equal(new List<string> { "Ctrl Shift X" }, both);
			Assert.Empty(HintsManager.BuildShortcutList(actions, Alt, null));

			// An unset key (-1) carries every modifier bit
			Assert.Empty(HintsManager.BuildShortcutList(actions, Ctrl | Alt | Shift, null));
		}

		[Fact]
		public void ShortcutListPutsModeActionsFirstAndOverlayCapsTheList()
		{
			BaseHintMode mode = new BaseHintMode();
			var actions = new List<UdbAction>();
			for(int i = 0; i < 14; i++) actions.Add(Bound(MakeAction("a" + i, Ctrl | ((int)Keys.A + i), 0, "", "Other " + i)));
			actions.Add(Bound(MakeAction("mine", Ctrl | (int)Keys.Z, 0, "", "Mine"), mode));

			var list = HintsManager.BuildShortcutList(actions, Ctrl, mode);
			Assert.Equal("Mine", list[0].Value);
			Assert.Equal("Other 0", list[1].Value);

			string rtf = HintsManager.RenderOverlay(Ctrl, null, list, n => n);
			Assert.Contains("... and " + (list.Count - HintsManager.MAX_OVERLAY_SHORTCUTS) + " more", rtf);
			Assert.DoesNotContain("Other 13", rtf);
			Assert.Null(HintsManager.RenderOverlay(Ctrl, new List<string>(), new List<KeyValuePair<string, string>>(), n => n));
		}

		[Fact]
		public void OverlayPersistsAcrossModeChangesAndClearsOnZero()
		{
			Harness h = new Harness(Cfg);
			h.Add(Bound(MakeAction("t_go", Ctrl | X, 0, "", "Go")));
			h.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.GENERAL);
			string basehints = h.Last;

			h.Manager.SetModifierOverlay(Ctrl);
			Assert.Contains("Ctrl held", h.Last);
			Assert.Contains("Ctrl does a base thing", h.Last);
			Assert.Contains("Go", h.Last);

			h.Manager.ShowHints(typeof(BaseHintMode), HintsManager.GENERAL);
			Assert.Contains("Ctrl held", h.Last);

			h.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.GENERAL);
			h.Manager.SetModifierOverlay(0);
			Assert.Equal(basehints, h.Last);
		}

		[Fact]
		public void EmptyOverlayKeepsTheNormalHints()
		{
			Harness h = new Harness(Cfg);
			h.Manager.ShowHints(typeof(DerivedHintMode), HintsManager.GENERAL);
			string basehints = h.Last;
			h.Manager.SetModifierOverlay(Alt);
			Assert.Equal(basehints, h.Last);
			Assert.Equal("mod_ctrl_alt_shift", HintsManager.ModifierGroupName(Ctrl | Alt | Shift));
		}

		[Fact]
		public void IsBeginBoundToMatchesOnlyTheDelegateTarget()
		{
			BaseHintMode mode = new BaseHintMode();
			UdbAction a = Bound(MakeAction("t", X), mode);
			Assert.True(a.IsBeginBoundTo(mode));
			Assert.False(a.IsBeginBoundTo(new BaseHintMode()));
			Assert.False(a.IsBeginBoundTo(null));
		}

		// Every class names a type in BuilderModes, every <k> names a real action, every group name is known
		[Fact]
		public void BuilderModesHintsCoverRealTypesAndActions()
		{
			Assembly modes = typeof(BuilderPlug).Assembly;
			Assembly core = typeof(UdbAction).Assembly;
			HashSet<string> actions = new HashSet<string>(ActionNames(core).Concat(ActionNames(modes)));
			Assert.True(actions.Contains("builder_classicselect") && actions.Contains("buildermodes_drawlinesmode"));
			HashSet<string> types = new HashSet<string>(modes.GetTypes().Select(t => t.Name));
			string[] mods = { "mod_ctrl", "mod_alt", "mod_shift", "mod_ctrl_alt", "mod_ctrl_shift", "mod_alt_shift", "mod_ctrl_alt_shift" };

			Dictionary<string, Dictionary<string, List<string>>> parsed;
			using(StreamReader reader = new StreamReader(Resource(modes, "Hints.cfg")))
				parsed = HintsManager.ParseHints(reader, "");

			List<string> problems = new List<string>();
			foreach(var cls in parsed)
			{
				if(!types.Contains(cls.Key)) problems.Add("no type " + cls.Key);
				foreach(var group in cls.Value)
				{
					if(group.Key.StartsWith("mod", StringComparison.Ordinal) && Array.IndexOf(mods, group.Key) < 0)
						problems.Add(cls.Key + ": bad group " + group.Key);
					foreach(string line in group.Value)
					{
						foreach(Match m in Regex.Matches(line, "<k>(.*?)</k>"))
							if(!actions.Contains(m.Groups[1].Value)) problems.Add(cls.Key + "." + group.Key + ": no action " + m.Groups[1].Value);
						if(Regex.Matches(line, "<k>").Count != Regex.Matches(line, "</k>").Count)
							problems.Add(cls.Key + "." + group.Key + ": unclosed <k>");
					}
				}
			}
			Assert.True(problems.Count == 0, string.Join("; ", problems.ToArray()));
		}

		private static Stream Resource(Assembly asm, string suffix)
		{
			string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
			return asm.GetManifestResourceStream(name);
		}

		private static IEnumerable<string> ActionNames(Assembly asm)
		{
			Configuration cfg = new Configuration();
			using(StreamReader reader = new StreamReader(Resource(asm, "Actions.cfg")))
				cfg.InputConfiguration(reader.ReadToEnd());
			Assert.False(cfg.ErrorResult);
			string prefix = asm.GetName().Name.ToLowerInvariant() + "_";
			List<string> names = new List<string>();
			foreach(DictionaryEntry e in cfg.Root) names.Add(prefix + e.Key);
			return names;
		}
	}
}
