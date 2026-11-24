
#region ================== Namespaces

using System;
using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Macros
{
	internal sealed class MacroStep
	{
		public string Action;
		public int Count;

		public MacroStep(string action, int count) { Action = action; Count = count; }
	}

	internal sealed class Macro
	{
		public const int MAX_SLOTS = 10;

		public string Name = "";

		// Class name of the editing mode it was recorded in
		public string Mode = "";
		public bool AnyMode;

		// 0 is none, else 1 to MAX_SLOTS
		public int Slot;

		public List<MacroStep> Steps = new List<MacroStep>();

		// The stored steps text when it didn't parse; kept as is so a save doesn't lose it
		public string MalformedSteps;

		public int StepCount
		{
			get
			{
				int n = 0;
				foreach(MacroStep s in Steps) n += s.Count;
				return n;
			}
		}

		public Macro Clone()
		{
			Macro m = new Macro { Name = Name, Mode = Mode, AnyMode = AnyMode, Slot = Slot, MalformedSteps = MalformedSteps };
			foreach(MacroStep s in Steps) m.Steps.Add(new MacroStep(s.Action, s.Count));
			return m;
		}

		// Step actions the editor doesn't have (a plugin is missing), each once
		public List<string> MissingActions(Func<string, bool> actionexists)
		{
			List<string> missing = new List<string>();
			foreach(MacroStep s in Steps)
				if(!actionexists(s.Action) && !missing.Contains(s.Action)) missing.Add(s.Action);
			return missing;
		}

		public bool IsAvailable(Func<string, bool> actionexists)
		{
			return (MalformedSteps == null) && (Steps.Count > 0) && (MissingActions(actionexists).Count == 0);
		}
	}
}
