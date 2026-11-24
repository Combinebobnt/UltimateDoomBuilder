
#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Macros
{
	// The saved macros, as the "macros" block of the program configuration
	internal sealed class MacroStore
	{
		#region ================== Constants

		internal const string SETTING = "macros";
		internal const int MAX_STEP_COUNT = 9999;

		private static readonly Regex KEYPATTERN = new Regex(@"^m([0-9]+)$", RegexOptions.CultureInvariant);
		private static readonly Regex STEPPATTERN = new Regex(@"^([a-z0-9_]+)(?:\*([0-9]+))?$", RegexOptions.CultureInvariant);

		#endregion

		#region ================== Variables

		private readonly List<Macro> macros = new List<Macro>();

		#endregion

		#region ================== Properties

		public List<Macro> Macros { get { return macros; } }

		#endregion

		#region ================== Step codec

		internal static string EncodeSteps(List<MacroStep> steps)
		{
			StringBuilder sb = new StringBuilder();
			foreach(MacroStep s in steps)
			{
				if(sb.Length > 0) sb.Append(',');
				sb.Append(s.Action);
				if(s.Count != 1) sb.Append('*').Append(s.Count.ToString(CultureInfo.InvariantCulture));
			}
			return sb.ToString();
		}

		internal static bool TryDecodeSteps(string text, out List<MacroStep> steps)
		{
			steps = new List<MacroStep>();
			if(string.IsNullOrEmpty(text)) return false;
			foreach(string part in text.Split(','))
			{
				Match m = STEPPATTERN.Match(part.Trim());
				if(!m.Success) return false;
				int count = 1;
				if(m.Groups[2].Success && !(int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count) && (count >= 1) && (count <= MAX_STEP_COUNT)))
					return false;
				steps.Add(new MacroStep(m.Groups[1].Value, count));
			}
			return true;
		}

		// Appends a recorded action, bumping the last step when it is the same action
		internal static void AppendStep(List<MacroStep> steps, string action)
		{
			if((steps.Count > 0) && (steps[steps.Count - 1].Action == action) && (steps[steps.Count - 1].Count < MAX_STEP_COUNT))
				steps[steps.Count - 1].Count++;
			else
				steps.Add(new MacroStep(action, 1));
		}

		#endregion

		#region ================== Load / Save

		// Entries that couldn't be read and were skipped, for the log
		public List<string> LoadErrors { get { return loaderrors; } }
		private readonly List<string> loaderrors = new List<string>();

		internal void Load(Configuration cfg)
		{
			macros.Clear();
			loaderrors.Clear();
			IDictionary block = cfg.ReadSettingObject(SETTING, null) as IDictionary;
			if(block == null)
			{
				if(cfg.SettingExists(SETTING)) loaderrors.Add("\"" + SETTING + "\" is not a block");
				return;
			}
			List<KeyValuePair<long, string>> keys = new List<KeyValuePair<long, string>>();
			foreach(DictionaryEntry e in block)
			{
				Match m = KEYPATTERN.Match(e.Key.ToString());
				long n;
				if(m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n))
					keys.Add(new KeyValuePair<long, string>(n, e.Key.ToString()));
			}
			keys.Sort((a, b) => a.Key.CompareTo(b.Key));

			foreach(KeyValuePair<long, string> k in keys)
			{
				string path = SETTING + "." + k.Value + ".";
				if(!(block[k.Value] is IDictionary)) { loaderrors.Add(k.Value + " is not a block"); continue; }
				Macro macro = new Macro();
				int slot;
				try
				{
					macro.Name = cfg.ReadSetting(path + "name", "").Trim();
					macro.Mode = cfg.ReadSetting(path + "mode", "");
					macro.AnyMode = cfg.ReadSetting(path + "anymode", false);
					slot = cfg.ReadSetting(path + "slot", 0);
				}
				catch(Exception e) when((e is FormatException) || (e is InvalidCastException) || (e is OverflowException))
				{
					loaderrors.Add(k.Value + ": " + e.Message);
					continue;
				}
				macro.Slot = ((slot >= 1) && (slot <= Macro.MAX_SLOTS)) ? slot : 0;

				string text = cfg.ReadSetting(path + "steps", "");
				List<MacroStep> steps;
				if(TryDecodeSteps(text, out steps)) macro.Steps = steps;
				else macro.MalformedSteps = text;

				if(macro.Name.Length == 0 || NameTaken(macro.Name, null)) macro.Name = UniqueName(macro.Name.Length == 0 ? "Macro" : macro.Name);
				if(macro.Slot != 0 && BySlot(macro.Slot) != null) macro.Slot = 0;
				macros.Add(macro);
			}
		}

		// Replaces the whole block; keys are renumbered in display order
		internal void Save(Configuration cfg)
		{
			if(cfg.SettingExists(SETTING)) cfg.DeleteSetting(SETTING);
			for(int i = 0; i < macros.Count; i++)
			{
				Macro m = macros[i];
				string path = SETTING + ".m" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".";
				cfg.WriteSetting(path + "name", m.Name);
				cfg.WriteSetting(path + "mode", m.Mode);
				cfg.WriteSetting(path + "anymode", m.AnyMode);
				cfg.WriteSetting(path + "slot", m.Slot);
				cfg.WriteSetting(path + "steps", m.MalformedSteps ?? EncodeSteps(m.Steps));
			}
		}

		#endregion

		#region ================== Editing

		internal bool NameTaken(string name, Macro except)
		{
			foreach(Macro m in macros)
				if(!ReferenceEquals(m, except) && string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		internal string UniqueName(string basename)
		{
			if(!NameTaken(basename, null)) return basename;
			for(int i = 2; ; i++)
			{
				string name = basename + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
				if(!NameTaken(name, null)) return name;
			}
		}

		internal Macro ByName(string name)
		{
			foreach(Macro m in macros)
				if(string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
			return null;
		}

		internal Macro BySlot(int slot)
		{
			if(slot == 0) return null;
			foreach(Macro m in macros) if(m.Slot == slot) return m;
			return null;
		}

		// Adds a new macro, or replaces "existing" with it in place. Refuses a duplicate name.
		internal bool Put(Macro macro, Macro existing)
		{
			macro.Name = macro.Name.Trim();
			if(macro.Name.Length == 0 || NameTaken(macro.Name, existing)) return false;
			int index = (existing != null) ? macros.IndexOf(existing) : -1;
			if(index >= 0) macros[index] = macro;
			else macros.Add(macro);
			AssignSlot(macro, macro.Slot);
			return true;
		}

		// A taken slot moves to this macro
		internal void AssignSlot(Macro macro, int slot)
		{
			if(slot < 0 || slot > Macro.MAX_SLOTS) slot = 0;
			if(slot != 0)
				foreach(Macro m in macros)
					if(!ReferenceEquals(m, macro) && m.Slot == slot) m.Slot = 0;
			macro.Slot = slot;
		}

		internal void Remove(Macro macro)
		{
			macros.Remove(macro);
		}

		internal void Move(Macro macro, int delta)
		{
			int index = macros.IndexOf(macro);
			int target = index + delta;
			if(index < 0 || target < 0 || target >= macros.Count) return;
			macros.RemoveAt(index);
			macros.Insert(target, macro);
		}

		#endregion
	}
}
