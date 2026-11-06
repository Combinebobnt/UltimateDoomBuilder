using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Data.Scripting;
using CodeImp.DoomBuilder.ZDoom;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	public class DecorateStateEndTests
	{
		private static readonly (string Text, string Class, string State, int Offset)[] Targets =
		{
			("Spawn", "", "spawn", 0),
			("Spawn+1", "", "spawn", 1),
			("Super::Spawn", "Parent", "spawn", 0),
			("Super::Spawn+1", "Parent", "spawn", 1),
			("Parent::Spawn+12", "parent", "spawn", 12),
			("\"Spawn\"", "", "spawn", 0),
			("\"Spawn\"+2", "", "spawn", 2),
			("\"Super::Spawn\"", "Parent", "spawn", 0),
			("\"Super::Spawn\"+2", "Parent", "spawn", 2),
			("Spawn + 1", "", "spawn", 1),
			("Spawn +1", "", "spawn", 1),
			("Spawn+\t1", "", "spawn", 1),
			("Super::Spawn + 3", "Parent", "spawn", 3),
			("\"Spawn\" + 2", "", "spawn", 2),
		};

		// ReadLine trims what's between Goto and the target, so this checks the rewind's column
		private static readonly string[] Leads = { " ", "   ", "\t", " \t " };

		private static readonly string[] Gaps = { "", " /* } */", "/* comment */", " // } }\n" };

		private static readonly string[] Closings = { " } }", "}}", " }}", "} }", "\t}\t}", " }\n}", "}\n}", "\n}}", "\n} }", "\n}\n}" };

		private static readonly string[] Trailers = { "", " ", " // } }", "/* } */" };

		private static readonly string[] Newlines = { "\n", "\r\n" };

		private static string Source(string end)
		{
			// Two actors end with the statement, because the old parser's damage carried on to the next ones
			return "actor Parent { States { Spawn: TNT1 A -1\n Stop\n } }\n"
				+ "actor First : Parent { States { Spawn: TNT1 A 1\n " + end + "\n"
				+ "actor Second : Parent { States { Spawn: TNT1 A 1\n " + end + "\n"
				+ "actor Last 12345 { States { Spawn: BOSS A -1\n Stop\n } }\n";
		}

		private static DecorateParser Parse(string source, out bool result)
		{
			var parser = new DecorateParser(new Dictionary<string, ActorStructure>()) { NoWarnings = true };

			// Parse() files actors under this resource, and its constructor needs a real DataReader
			var resource = (ScriptResource)RuntimeHelpers.GetUninitializedObject(typeof(ScriptResource));
			typeof(ScriptResource).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(resource, new HashSet<string>());
			parser.ScriptResources[string.Empty] = resource;

			// One byte per char, so a source string can spell out any byte of a file
			var stream = new MemoryStream(source.Select(c => (byte)c).ToArray());
			var data = new TextResourceData(stream, new DataLocation(DataLocation.RESOURCE_DIRECTORY, "test", false, false, false, new List<string>()), "DECORATE");
			result = parser.Parse(data, true);
			return parser;
		}

		private static string FindDamage(DecorateParser parser, bool result)
		{
			// Empty means the statement's actors and everything after them came through whole
			if(!result) return "parse failed: " + parser.ErrorDescription;

			foreach(string name in new[] { "first", "second" })
			{
				if(!parser.AllActorsByClass.TryGetValue(name, out ActorStructure? actor)) return name + " lost";
				if(actor.states.Count != 1) return name + " has " + actor.states.Count + " states";
				if(actor.states["spawn"].SpritesCount != 1) return name + " has " + actor.states["spawn"].SpritesCount + " sprites";
			}

			if(!parser.AllActorsByClass.TryGetValue("last", out ActorStructure? last)) return "last lost";
			return (last.DoomEdNum == 12345) ? "" : "last absorbed";
		}

		private static string FindWrongTarget(ActorStructure actor, string classname, string state, int offset)
		{
			StateGoto target = actor.states["spawn"].gotostate;
			if(target == null) return actor.ClassName + " has no goto";

			string expected = ((classname.Length > 0) ? classname : actor.ClassName) + "::" + state + "+" + offset;
			string actual = target.ClassName + "::" + target.StateName + "+" + target.SpriteOffset;
			return (actual == expected) ? "" : actor.ClassName + " goes to " + actual + ", not " + expected;
		}

		private static void AssertNoFailures(List<string> failures, int total)
		{
			Assert.True(failures.Count == 0, failures.Count + " of " + total + " failed:\n" + string.Join("\n", failures.Take(10)));
		}

		[Fact]
		public void Goto_ThenClosingBraces_KeepsTargetAndFollowingActors()
		{
			var failures = new List<string>();
			int total = 0;

			foreach(var target in Targets)
			foreach(string lead in Leads)
			foreach(string gap in Gaps)
			foreach(string closing in Closings)
			foreach(string trailer in Trailers)
			foreach(string newline in Newlines)
			{
				string end = "Goto" + lead + target.Text + gap + closing + trailer;
				DecorateParser parser = Parse(Source(end).Replace("\n", newline), out bool result);
				total++;

				string problem = FindDamage(parser, result);
				if(problem.Length == 0) problem = FindWrongTarget(parser.AllActorsByClass["first"], target.Class, target.State, target.Offset);
				if(problem.Length == 0) problem = FindWrongTarget(parser.AllActorsByClass["second"], target.Class, target.State, target.Offset);
				if(problem.Length > 0) failures.Add(problem + " <- " + end.Replace("\n", "\\n") + " (" + newline.Length + " byte newline)");
			}

			AssertNoFailures(failures, total);
		}

		[Fact]
		public void FlowKeyword_ThenClosingBraces_KeepsFollowingActors()
		{
			var failures = new List<string>();
			int total = 0;

			foreach(string keyword in new[] { "stop", "Stop", "STOP", "Loop", "wait", "Wait", "fail", "Fail" })
			foreach(string gap in Gaps)
			foreach(string closing in Closings)
			foreach(string trailer in Trailers)
			foreach(string newline in Newlines)
			{
				string end = keyword + gap + closing + trailer;
				DecorateParser parser = Parse(Source(end).Replace("\n", newline), out bool result);
				total++;

				string problem = FindDamage(parser, result);
				if(problem.Length > 0) failures.Add(problem + " <- " + end.Replace("\n", "\\n") + " (" + newline.Length + " byte newline)");
			}

			AssertNoFailures(failures, total);
		}

		[Theory]
		// A stray word stops the target loops before they reach the brace
		[InlineData("Goto Spawn extra more } }")]
		[InlineData("Goto /* comment */ Spawn } }")]
		public void Goto_BraceBeyondTheTarget_KeepsFollowingActors(string end)
		{
			DecorateParser parser = Parse(Source(end), out bool result);

			Assert.Equal("", FindDamage(parser, result));
		}

		[Theory]
		[InlineData("Goto Spawn // }", "spawn")]
		[InlineData("Goto Spawn // comment } }", "spawn")]
		[InlineData("Goto Spawn /* } */", "spawn")]
		[InlineData("Goto Spawn+1 // }", "spawn")]
		[InlineData("Goto \"Spa}wn\"", "spa}wn")]
		[InlineData("Goto \"Spa}wn\" // }", "spa}wn")]
		public void Goto_BraceInCommentOrQuotes_DoesNotEndTheState(string statement, string state)
		{
			string source = "actor First { States { Spawn: TNT1 A 1\n " + statement + "\n Death: TNT1 A 1\n Stop\n } }\n";
			DecorateParser parser = Parse(source, out bool result);

			Assert.True(result, parser.ErrorDescription);
			ActorStructure actor = parser.AllActorsByClass["first"];
			Assert.True(actor.states.ContainsKey("death"), "the state ended at the brace");
			Assert.Equal(state, actor.states["spawn"].gotostate.StateName);
		}

		[Theory]
		[InlineData("// caf\xC3\xA9\n", "Goto Spawn}}")]
		// ReadLine trims this 0xA0, as it's the first byte read: one further on it decodes to a non-space
		[InlineData("", "Goto \xA0Spawn}}")]
		[InlineData("", "Goto  \xA0Spawn}}")]
		[InlineData("", "Goto \"Sp\xC3\xA9wn\"}}")]
		[InlineData("", "Goto Spawn\xFF}}")]
		public void Goto_NonAsciiBytes_DoNotShiftTheRewind(string prefix, string end)
		{
			DecorateParser parser = Parse(prefix + Source(end), out bool result);

			Assert.Equal("", FindDamage(parser, result));
		}

		[Theory]
		// Sometimes these names are real sprites (Skulltag, Zandronum)
		[InlineData("Fail A 1\n Stop\n } }", "FAILA")]
		[InlineData("Fail A 1 } }", "FAILA")]
		[InlineData("Fail\tB 2 Bright\n Stop } }", "FAILB")]
		[InlineData("Loop C -1\n Stop}}", "LOOPC")]
		[InlineData("Wait D 1\n Goto Spawn } }", "WAITD")]
		public void FlowKeyword_WithFrames_IsStillASprite(string statements, string sprite)
		{
			string source = "actor First { States { Spawn: " + statements + "\nactor Last { }\n";
			DecorateParser parser = Parse(source, out bool result);

			Assert.True(result, parser.ErrorDescription);
			Assert.Contains("last", parser.AllActorsByClass.Keys);
			Assert.Equal(sprite, parser.AllActorsByClass["first"].states["spawn"].GetSprite(0).Sprite);
		}

		[Theory]
		// The block never closes, so this is a truncated file, unlike a keyword before }
		[InlineData("Stop")]
		[InlineData("Loop ")]
		[InlineData("fail // comment")]
		public void FlowKeyword_AtEndOfFile_StaysAnError(string end)
		{
			Parse("actor First { States { Spawn: TNT1 A 1\n " + end, out bool result);

			Assert.False(result);
		}

		[Theory]
		// A bare Goto as the file's last bytes leaves ReadLine nothing to return
		[InlineData("Goto")]
		[InlineData("Goto ")]
		public void Goto_AtEndOfFile_EndsLikeAGotoWithATarget(string end)
		{
			const string start = "actor First { States { Spawn: TNT1 A 1\n ";
			Parse(start + "Goto Spawn", out bool expected);
			Parse(start + end, out bool result);

			Assert.Equal(expected, result);
		}

		[Fact]
		public void Bom_AtFileStart_KeepsFirstActor()
		{
			DecorateParser parser = Parse("\xEF\xBB\xBF" + Source("Goto Spawn } }"), out bool result);

			Assert.Equal("", FindDamage(parser, result));
			Assert.Contains("parent", parser.AllActorsByClass.Keys);
		}

		[Theory]
		[InlineData("Goto Spawn /* start", "spawn", 0)]
		[InlineData("Goto Spawn/*", "spawn", 0)]
		[InlineData("Goto Spawn+1 /* } }", "spawn", 1)]
		[InlineData("Goto Spawn + 2 /* a */ /* b", "spawn", 2)]
		[InlineData("Goto \"Spawn\" /* \"", "spawn", 0)]
		[InlineData("Goto Super::Spawn /* start", "spawn", 0)]
		public void Goto_BlockCommentLeftOpen_HidesItsLaterLines(string statement, string state, int offset)
		{
			// The comment's lines would read as a frame and a state if the parser saw them
			string source = "actor First { States { Spawn: TNT1 A 1\n " + statement + "\n BOSS A 1\n Bogus: BOSS B 1\n } }\n actor Fake { }\n */\n"
				+ " Death: TNT1 A 1\n Stop\n } }\nactor Last { }\n";
			DecorateParser parser = Parse(source, out bool result);

			Assert.True(result, parser.ErrorDescription);
			ActorStructure actor = parser.AllActorsByClass["first"];
			Assert.Equal("death,spawn", string.Join(",", actor.states.Keys.OrderBy(k => k)));
			Assert.Equal(1, actor.states["spawn"].SpritesCount);
			Assert.Equal(state, actor.states["spawn"].gotostate.StateName);
			Assert.Equal(offset, actor.states["spawn"].gotostate.SpriteOffset);
			Assert.DoesNotContain("fake", parser.AllActorsByClass.Keys);
			Assert.Contains("last", parser.AllActorsByClass.Keys);
		}
	}
}
