using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Types;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	// Swaps General.map, so it must not run beside other tests that do
	[Collection("GeneralStatics")]
	public class ThingsFilterRuleTests
	{
		// Thing's constructor compares its MapSet with General.Map.Map, which needs a map manager
		private sealed class FakeMap : IDisposable
		{
			private readonly object previousmap;

			public FakeMap()
			{
				FieldInfo field = typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!;
				previousmap = field.GetValue(null);
				var map = (MapManager)RuntimeHelpers.GetUninitializedObject(typeof(MapManager));
				GC.SuppressFinalize(map);
				field.SetValue(null, map);
			}

			public void Dispose()
			{
				typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, previousmap);
			}
		}

		private static void SetField(object o, string name, object value)
		{
			o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(o, value);
		}

		private static Thing MakeThing(int type = 1, int tag = 0, int action = 0, double z = 0, int angledoom = 0, int[]? args = null)
		{
			Thing t = new Thing(new MapSet(), 0, false);
			SetField(t, "type", type);
			SetField(t, "tag", tag);
			SetField(t, "action", action);
			SetField(t, "pos", new Vector3D(0, 0, z));
			SetField(t, "angledoom", angledoom);
			SetField(t, "anglerad", Angle2D.DoomToReal(angledoom));
			if(args != null) Array.Copy(args, t.Args, args.Length);
			return t;
		}

		private static ThingTypeInfo MakeInfo(string category = "monsters", string title = "Imp", string classname = "DoomImp", bool known = true)
		{
			ThingTypeInfo ti = new ThingTypeInfo(new ThingCategory(null, category, category, null), 3001, title);
			SetField(ti, "classname", classname);
			SetField(ti, "isknown", known);
			return ti;
		}

		private static ThingsFilter MakeFilter(string name, ThingsFilterMatchMode mode, params ThingsFilterRule[] rules)
		{
			ThingsFilter f = new ThingsFilter();
			f.Name = name;
			f.MatchMode = mode;
			f.Rules.AddRange(rules);
			return f;
		}

		private static ThingsFilterRule Rule(ThingsFilterRuleSubject s, ThingsFilterRuleOperator o, string key = "", string value = "")
		{
			return new ThingsFilterRule(s, o, key, value);
		}

		private static ThingsFilterContext Ctx(params ThingsFilter[] filters)
		{
			ThingsFilterContext ctx = new ThingsFilterContext();
			ctx.Filters = new List<ThingsFilter>(filters);
			return ctx;
		}

		private static ThingsFilterRuleOutcome Eval(ThingsFilterRule r, Thing t, ThingTypeInfo? ti = null, ThingsFilterContext? ctx = null)
		{
			return r.Evaluate(t, ti ?? MakeInfo(), ctx ?? Ctx());
		}

		[Fact]
		public void NumberList_ParsesSinglesListsAndBothRangeForms()
		{
			var cases = new Dictionary<string, int[]>
			{
				{ "5", new[] { 5, 5 } },
				{ " 1 , 3-4 ", new[] { 1, 1, 3, 4 } },
				{ "-10..-1", new[] { -10, -1 } },
				{ "-5--1", new[] { -5, -1 } },
				{ "-3", new[] { -3, -3 } },
				{ "3001-3006, 9,", new[] { 3001, 3006, 9, 9 } },
			};
			List<string> failures = new List<string>();
			foreach(var c in cases)
			{
				string error;
				var ranges = ThingsFilterRule.ParseNumberList(c.Key, out error);
				string got = (ranges == null ? "null: " + error : string.Join(",", ranges.SelectMany(r => new[] { r.Key, r.Value })));
				if(got != string.Join(",", c.Value)) failures.Add("\"" + c.Key + "\" -> " + got);
			}
			foreach(string bad in new[] { "", " , ", "abc", "1-", "5-3", "1.5", "1 2", "--1" })
			{
				string error;
				if(ThingsFilterRule.ParseNumberList(bad, out error) != null || string.IsNullOrEmpty(error)) failures.Add("\"" + bad + "\" parsed");
			}
			Assert.True(failures.Count == 0, string.Join("; ", failures));
		}

		[Fact]
		public void NumberSubjects_EachOperator()
		{
			using(new FakeMap())
			{
				Thing t = MakeThing(type: 3002, tag: 15, action: 80, z: 64.7, angledoom: 90, args: new[] { 0, 0, 7, 0, 0 });
				var table = new (ThingsFilterRuleSubject s, ThingsFilterRuleOperator o, string key, string value, ThingsFilterRuleOutcome want)[]
				{
					(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "3001-3006, 9", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsNotOneOf, "", "3001-3006", ThingsFilterRuleOutcome.Fail),
					(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.IsOneOf, "", "10-20", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.Less, "", "15", ThingsFilterRuleOutcome.Fail),
					(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.LessOrEqual, "", "15", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Action, ThingsFilterRuleOperator.Greater, "", "79", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Action, ThingsFilterRuleOperator.GreaterOrEqual, "", "81", ThingsFilterRuleOutcome.Fail),
					(ThingsFilterRuleSubject.ZHeight, ThingsFilterRuleOperator.Greater, "", "63", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.ZHeight, ThingsFilterRuleOperator.IsOneOf, "", "64", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Angle, ThingsFilterRuleOperator.IsOneOf, "", "90", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Arg, ThingsFilterRuleOperator.IsOneOf, "2", "7", ThingsFilterRuleOutcome.Pass),
					(ThingsFilterRuleSubject.Arg, ThingsFilterRuleOperator.IsOneOf, "1", "7", ThingsFilterRuleOutcome.Fail),
					// Broken rules fail
					(ThingsFilterRuleSubject.Arg, ThingsFilterRuleOperator.IsOneOf, "5", "7", ThingsFilterRuleOutcome.Fail),
					(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.Less, "", "1,2", ThingsFilterRuleOutcome.Fail),
					(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.Less, "", "5000", ThingsFilterRuleOutcome.Fail),
				};
				List<string> failures = new List<string>();
				foreach(var c in table)
				{
					var got = Eval(Rule(c.s, c.o, c.key, c.value), t);
					if(got != c.want) failures.Add(c.s + " " + c.o + " " + c.key + " \"" + c.value + "\": " + got);
				}
				Assert.True(failures.Count == 0, string.Join("; ", failures));
				Assert.False(Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.Less, "", "5").IsValidRule);
				Assert.False(Rule(ThingsFilterRuleSubject.Arg, ThingsFilterRuleOperator.IsOneOf, "x", "1").IsValidRule);
			}
		}

		[Fact]
		public void TextSubjects_AreCaseInsensitiveWithWildcards()
		{
			using(new FakeMap())
			{
				Thing t = MakeThing();
				ThingTypeInfo ti = MakeInfo(title: "Red Torch", classname: "RedTorchLight");
				var table = new (ThingsFilterRuleSubject s, ThingsFilterRuleOperator o, string value, bool pass)[]
				{
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.Equals, "redtorchlight", true),
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.NotEquals, "redtorchlight", false),
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.Contains, "LIGHT", true),
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.Matches, "*light*", true),
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.Matches, "red?orch*", true),
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.Matches, "torch*", false),
					(ThingsFilterRuleSubject.ClassName, ThingsFilterRuleOperator.Matches, "Red.Torch*", false),
					(ThingsFilterRuleSubject.Title, ThingsFilterRuleOperator.Equals, "red torch", true),
					(ThingsFilterRuleSubject.Title, ThingsFilterRuleOperator.Contains, "blue", false),
				};
				List<string> failures = new List<string>();
				foreach(var c in table)
				{
					bool got = Eval(Rule(c.s, c.o, "", c.value), t, ti) == ThingsFilterRuleOutcome.Pass;
					if(got != c.pass) failures.Add(c.s + " " + c.o + " \"" + c.value + "\"");
				}
				Assert.True(failures.Count == 0, string.Join("; ", failures));
			}
		}

		[Fact]
		public void FlagsFieldsUnknownTypeAndFormatGating()
		{
			using(new FakeMap())
			{
				Thing t = MakeThing();
				t.Flags["skill1"] = true;
				t.Flags["ambush"] = false;
				t.Fields.Add("user_hp", new UniValue(UniversalType.Integer, 50));
				t.Fields.Add("comment", new UniValue(UniversalType.String, "Boss Room"));
				t.Fields.Add("user_on", new UniValue(UniversalType.Boolean, true));

				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(Rule(ThingsFilterRuleSubject.Flag, ThingsFilterRuleOperator.IsTrue, "skill1"), t));
				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(Rule(ThingsFilterRuleSubject.Flag, ThingsFilterRuleOperator.IsFalse, "ambush"), t));
				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(Rule(ThingsFilterRuleSubject.Flag, ThingsFilterRuleOperator.IsFalse, "dm"), t));

				var table = new (ThingsFilterRuleOperator o, string key, string value, bool pass)[]
				{
					(ThingsFilterRuleOperator.Exists, "user_hp", "", true),
					(ThingsFilterRuleOperator.IsAbsent, "user_hp", "", false),
					(ThingsFilterRuleOperator.IsAbsent, "user_none", "", true),
					(ThingsFilterRuleOperator.Equals, "user_hp", "50", true),
					(ThingsFilterRuleOperator.Equals, "user_hp", "50.0", true),
					(ThingsFilterRuleOperator.NotEquals, "user_hp", "5", true),
					(ThingsFilterRuleOperator.Greater, "user_hp", "49.5", true),
					(ThingsFilterRuleOperator.Less, "user_hp", "50", false),
					(ThingsFilterRuleOperator.Equals, "comment", "boss room", true),
					(ThingsFilterRuleOperator.Contains, "comment", "ROOM", true),
					(ThingsFilterRuleOperator.Equals, "user_on", "True", true),
					(ThingsFilterRuleOperator.Equals, "user_none", "1", false),
					(ThingsFilterRuleOperator.NotEquals, "user_none", "1", true),
				};
				List<string> failures = new List<string>();
				foreach(var c in table)
				{
					bool got = Eval(Rule(ThingsFilterRuleSubject.Field, c.o, c.key, c.value), t) == ThingsFilterRuleOutcome.Pass;
					if(got != c.pass) failures.Add(c.o + " " + c.key + " \"" + c.value + "\"");
				}
				Assert.True(failures.Count == 0, string.Join("; ", failures));
				Assert.False(Rule(ThingsFilterRuleSubject.Field, ThingsFilterRuleOperator.Greater, "user_hp", "abc").IsValidRule);

				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(Rule(ThingsFilterRuleSubject.UnknownType, ThingsFilterRuleOperator.IsTrue), t, MakeInfo(known: false)));
				Assert.Equal(ThingsFilterRuleOutcome.Fail, Eval(Rule(ThingsFilterRuleSubject.UnknownType, ThingsFilterRuleOperator.IsTrue), t, MakeInfo()));

				// Subjects the map format lacks, and flags it doesn't define, are skipped
				ThingsFilterContext doom = Ctx();
				doom.HasThingTag = doom.HasThingAction = doom.HasActionArgs = doom.HasThingHeight = doom.HasCustomFields = false;
				doom.KnownFlags = new List<string> { "skill1" };
				Assert.Equal(ThingsFilterRuleOutcome.Skip, Eval(Rule(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.IsOneOf, "", "1"), t, null, doom));
				Assert.Equal(ThingsFilterRuleOutcome.Skip, Eval(Rule(ThingsFilterRuleSubject.Arg, ThingsFilterRuleOperator.IsOneOf, "0", "1"), t, null, doom));
				Assert.Equal(ThingsFilterRuleOutcome.Skip, Eval(Rule(ThingsFilterRuleSubject.Field, ThingsFilterRuleOperator.Exists, "user_hp"), t, null, doom));
				Assert.Equal(ThingsFilterRuleOutcome.Skip, Eval(Rule(ThingsFilterRuleSubject.Flag, ThingsFilterRuleOperator.IsTrue, "dm"), t, null, doom));
				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(Rule(ThingsFilterRuleSubject.Flag, ThingsFilterRuleOperator.IsTrue, "skill1"), t, null, doom));
			}
		}

		[Fact]
		public void MatchModes_SkipsAndInvert()
		{
			using(new FakeMap())
			{
				Thing imp = MakeThing(type: 3001);
				ThingTypeInfo ti = MakeInfo();
				ThingsFilterRule isimp = Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "3001");
				ThingsFilterRule isdemon = Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "3002");
				ThingsFilterRule tagged = Rule(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.IsOneOf, "", "5");
				ThingsFilterRule broken = Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "x");
				ThingsFilterContext notags = Ctx();
				notags.HasThingTag = false;

				Assert.False(MakeFilter("a", ThingsFilterMatchMode.All, isimp, isdemon).Qualifies(imp, ti, Ctx()));
				Assert.True(MakeFilter("a", ThingsFilterMatchMode.Any, isimp, isdemon).Qualifies(imp, ti, Ctx()));
				Assert.True(MakeFilter("a", ThingsFilterMatchMode.All).Qualifies(imp, ti, Ctx()));
				Assert.True(MakeFilter("a", ThingsFilterMatchMode.Any, tagged).Qualifies(imp, ti, notags), "all-skip list passes");
				Assert.False(MakeFilter("a", ThingsFilterMatchMode.Any, tagged, isdemon).Qualifies(imp, ti, notags), "a skipped rule must not make Any pass");
				Assert.True(MakeFilter("a", ThingsFilterMatchMode.All, tagged, isimp).Qualifies(imp, ti, notags));

				ThingsFilter inverted = MakeFilter("a", ThingsFilterMatchMode.All, isimp);
				inverted.Invert = true;
				Assert.False(inverted.Qualifies(imp, ti, Ctx()));
				ThingsFilter invertedbroken = MakeFilter("a", ThingsFilterMatchMode.All, broken);
				invertedbroken.Invert = true;
				Assert.True(invertedbroken.Qualifies(imp, ti, Ctx()), "a broken rule fails before invert");
				Assert.True(MakeFilter("a", ThingsFilterMatchMode.Any, broken, isimp).Qualifies(imp, ti, Ctx()));

				// The old property tabs still AND with the rules
				ThingsFilter both = MakeFilter("a", ThingsFilterMatchMode.Any, isimp);
				both.ThingTag = 9;
				Assert.False(both.Qualifies(imp, ti, Ctx()));
			}
		}

		[Fact]
		public void Subcategories_MatchChildrenOnly()
		{
			using(new FakeMap())
			{
				Thing t = MakeThing();
				ThingsFilter f = new ThingsFilter();
				f.CategoryName = "Monsters";
				f.IncludeSubcategories = true;
				Assert.True(f.Qualifies(t, MakeInfo(category: "monsters.zombies"), Ctx()));
				Assert.True(f.Qualifies(t, MakeInfo(category: "monsters"), Ctx()));
				Assert.False(f.Qualifies(t, MakeInfo(category: "monstersx.zombies"), Ctx()));
				Assert.False(f.Qualifies(t, MakeInfo(category: "monstersx"), Ctx()));
				f.IncludeSubcategories = false;
				Assert.False(f.Qualifies(t, MakeInfo(category: "monsters.zombies"), Ctx()));

				ThingsFilterRule incat = Rule(ThingsFilterRuleSubject.Category, ThingsFilterRuleOperator.Equals, "", "MONSTERS");
				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(incat, t, MakeInfo(category: "monsters.zombies")));
				Assert.Equal(ThingsFilterRuleOutcome.Fail, Eval(incat, t, MakeInfo(category: "monstersx")));
				incat.IncludeSubcategories = false;
				Assert.Equal(ThingsFilterRuleOutcome.Fail, Eval(incat, t, MakeInfo(category: "monsters.zombies")));
				ThingsFilterRule notcat = Rule(ThingsFilterRuleSubject.Category, ThingsFilterRuleOperator.NotEquals, "", "monsters");
				Assert.Equal(ThingsFilterRuleOutcome.Pass, Eval(notcat, t, MakeInfo(category: "lights")));
			}
		}

		[Fact]
		public void MatchesFilter_ResolvesFirstNameAndSurvivesCycles()
		{
			using(new FakeMap())
			{
				Thing imp = MakeThing(type: 3001);
				ThingTypeInfo ti = MakeInfo();
				ThingsFilterRule isimp = Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "3001");
				ThingsFilterRule isdemon = Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "3002");

				ThingsFilter imps = MakeFilter("Imps", ThingsFilterMatchMode.All, isimp);
				ThingsFilter dup = MakeFilter("Imps", ThingsFilterMatchMode.All, isdemon);
				ThingsFilter user = MakeFilter("User", ThingsFilterMatchMode.All, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsTrue, "Imps"));
				ThingsFilter notuser = MakeFilter("NotUser", ThingsFilterMatchMode.All, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsFalse, "Imps"));
				ThingsFilterContext ctx = Ctx(imps, dup, user, notuser);
				Assert.True(user.Qualifies(imp, ti, ctx));
				Assert.False(notuser.Qualifies(imp, ti, ctx));
				Assert.False(user.Qualifies(imp, ti, Ctx(dup, imps, user)), "first match in list order wins");
				Assert.False(user.HasInvalidRules(ctx));

				ThingsFilter missing = MakeFilter("M", ThingsFilterMatchMode.All, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsFalse, "Gone"));
				Assert.False(missing.Qualifies(imp, ti, Ctx(missing)));
				Assert.True(missing.HasInvalidRules(Ctx(missing)));

				ThingsFilter a = MakeFilter("A", ThingsFilterMatchMode.Any, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsFalse, "B"));
				ThingsFilter b = MakeFilter("B", ThingsFilterMatchMode.Any, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsFalse, "A"));
				ThingsFilterContext cyc = Ctx(a, b);
				a.Qualifies(imp, ti, cyc);
				Assert.Empty(cyc.Visiting);
				Assert.True(a.HasInvalidRules(cyc));
				Assert.True(b.HasInvalidRules(cyc));
				ThingsFilter self = MakeFilter("S", ThingsFilterMatchMode.All, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsFalse, "S"));
				Assert.False(self.Qualifies(imp, ti, Ctx(self)));
				Assert.True(self.HasInvalidRules(Ctx(self)));

				// A->B->D and A->C->D share D without a cycle
				ThingsFilter d = MakeFilter("D", ThingsFilterMatchMode.All, isimp);
				ThingsFilter bd = MakeFilter("BD", ThingsFilterMatchMode.All, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsTrue, "D"));
				ThingsFilter cd = MakeFilter("CD", ThingsFilterMatchMode.All, Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsTrue, "D"));
				ThingsFilter top = MakeFilter("Top", ThingsFilterMatchMode.All,
					Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsTrue, "BD"),
					Rule(ThingsFilterRuleSubject.MatchesFilter, ThingsFilterRuleOperator.IsTrue, "CD"));
				ThingsFilterContext diamond = Ctx(top, bd, cd, d);
				Assert.True(top.Qualifies(imp, ti, diamond));
				Assert.False(top.HasInvalidRules(diamond));
			}
		}

		[Fact]
		public void CopyConstructor_CopiesRulesDeeply()
		{
			using(new FakeMap())
			{
				ThingsFilter original = MakeFilter("a", ThingsFilterMatchMode.Any, Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", "1"));
				original.IncludeSubcategories = false;
				ThingsFilter copy = new ThingsFilter(original);
				Assert.Equal(ThingsFilterMatchMode.Any, copy.MatchMode);
				Assert.False(copy.IncludeSubcategories);
				copy.Rules[0].Value = "2";
				copy.Rules.Add(Rule(ThingsFilterRuleSubject.Tag, ThingsFilterRuleOperator.IsOneOf, "", "1"));
				Assert.Equal("1", original.Rules[0].Value);
				Assert.Equal(1, original.Rules.Count);
			}
		}

		[Fact]
		public void Settings_RoundTripKeepsOrderAndUnknownValues()
		{
			using(new FakeMap())
			{
				ThingsFilter f = MakeFilter("Many", ThingsFilterMatchMode.Any);
				f.IncludeSubcategories = true;
				for(int i = 0; i < 12; i++)
					f.Rules.Add(Rule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", i.ToString()));
				f.Rules.Add(Rule(ThingsFilterRuleSubject.Field, ThingsFilterRuleOperator.Contains, "comment", "a \"quoted\" value; here"));

				Configuration cfg = new Configuration(true);
				f.WriteSettings(cfg, "thingsfilters.filter0");
				Configuration back = new Configuration(true);
				Assert.True(back.InputConfiguration(cfg.OutputConfiguration(), true), back.ErrorDescription);
				ThingsFilter read = new ThingsFilter(back, "thingsfilters.filter0");
				Assert.Equal(ThingsFilterMatchMode.Any, read.MatchMode);
				Assert.True(read.IncludeSubcategories);
				Assert.Equal(f.Rules.Select(r => r.ToString()), read.Rules.Select(r => r.ToString()));

				string hand = "thingsfilters { old { name = \"Old\"; type = 3001; }\n"
					+ "odd { name = \"Odd\"; matchmode = 7; rules { rule0 { subject = 2; operator = 4; key = \"\"; value = 64; }\n"
					+ "rule1 { subject = 99; operator = 42; key = \"k\"; value = \"v\"; subcategories = true; } } } }";
				Configuration hc = new Configuration(true);
				Assert.True(hc.InputConfiguration(hand, true), hc.ErrorDescription);

				ThingsFilter old = new ThingsFilter(hc, "thingsfilters.old");
				Assert.Equal(ThingsFilterMatchMode.All, old.MatchMode);
				Assert.False(old.IncludeSubcategories);
				Assert.Empty(old.Rules);
				Assert.Equal(3001, old.ThingType);

				ThingsFilter odd = new ThingsFilter(hc, "thingsfilters.odd");
				Assert.Equal(ThingsFilterMatchMode.All, odd.MatchMode);
				Assert.Equal(2, odd.Rules.Count);
				Assert.Equal("64", odd.Rules[0].Value);
				Assert.True(odd.Rules[0].IsValidRule);
				Assert.False(odd.Rules[1].IsValidRule);
				Assert.True(odd.HasInvalidRules(Ctx(odd)));

				Configuration rewritten = new Configuration(true);
				odd.WriteSettings(rewritten, "f");
				Assert.Equal(99, rewritten.ReadSetting("f.rules.rule1.subject", 0));
				Assert.Equal(42, rewritten.ReadSetting("f.rules.rule1.operator", 0));
				Assert.Equal("k", rewritten.ReadSetting("f.rules.rule1.key", ""));
				Assert.True(rewritten.ReadSetting("f.rules.rule1.subcategories", false));
			}
		}
	}
}
