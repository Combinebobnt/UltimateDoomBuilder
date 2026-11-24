using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Macros;
using Xunit;

namespace Builder.Tests
{
	// Recording a step that can't replay (a mouse drag, a nested call) makes a macro that silently does the wrong thing
	public class MacroRecordingTests
	{
		private const string RAISE = "buildermodes_raisefloor8";

		[Fact]
		public void ClassifyDecisionTable()
		{
			int scroll = (int)SpecialKeys.MScrollUp;
			int ctrlclick = (int)(Keys.LButton | Keys.Control);
			// name, key, depth, volatile before, volatile after, bound -> record, has reason
			object[][] cases =
			{
				new object[] { RAISE, 0, 0, false, false, true, true, false },
				new object[] { RAISE, (int)Keys.PageUp, 0, false, false, true, true, false },
				new object[] { RAISE, scroll, 0, false, false, true, true, false },
				new object[] { RAISE, ctrlclick, 0, false, false, true, false, true },
				new object[] { RAISE, (int)Keys.XButton2, 0, false, false, true, false, true },
				new object[] { RAISE, 0, 1, false, false, true, false, false },
				new object[] { RAISE, 0, 0, true, false, true, false, true },
				new object[] { RAISE, 0, 0, false, true, true, false, true },
				new object[] { RAISE, 0, 0, false, false, false, false, true },
				new object[] { "builder_undo", 0, 0, false, false, true, false, true },
				new object[] { "builder_macroslot3", 0, 0, false, false, true, false, false },
			};
			List<string> problems = new List<string>();
			foreach(object[] c in cases)
			{
				MacroClassification r = MacroRecording.Classify((string)c[0], (int)c[1], (int)c[2], (bool)c[3], (bool)c[4], (bool)c[5]);
				if(r.Record != (bool)c[6] || (r.Reason != null) != (bool)c[7])
					problems.Add(string.Join(",", c.Select(o => o.ToString()).ToArray()) + " -> " + r.Record + "/" + r.Reason);
			}
			Assert.True(problems.Count == 0, string.Join("; ", problems.ToArray()));
		}

		// A renamed action would otherwise drop out of the denylist unnoticed
		[Fact]
		public void EveryDenylistedNameIsAnAction()
		{
			HashSet<string> actions = new HashSet<string>(ActionNames(typeof(General).Assembly).Concat(ActionNames(typeof(CodeImp.DoomBuilder.BuilderModes.BuilderPlug).Assembly)));
			List<string> missing = MacroRecording.NotRecordable.Where(n => !actions.Contains(n)).ToList();
			Assert.True(missing.Count == 0, "not actions: " + string.Join(", ", missing.ToArray()));
		}

		private static IEnumerable<string> ActionNames(Assembly asm)
		{
			string res = asm.GetManifestResourceNames().Single(n => n.EndsWith(".Actions.cfg", StringComparison.OrdinalIgnoreCase));
			Configuration cfg = new Configuration();
			using(StreamReader reader = new StreamReader(asm.GetManifestResourceStream(res)))
				cfg.InputConfiguration(reader.ReadToEnd());
			Assert.False(cfg.ErrorResult);
			string prefix = asm.GetName().Name.ToLowerInvariant() + "_";
			List<string> names = new List<string>();
			foreach(DictionaryEntry e in cfg.Root) names.Add(prefix + e.Key);
			return names;
		}
	}
}
