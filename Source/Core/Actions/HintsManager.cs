#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Actions
{
	public class HintsManager
	{
		#region ================== Public constants

		public const string GENERAL = "general";
		public const string MULTISELECTION = "multiselection";

		#endregion

		#region ================== Constants

		private const string HINTS_RESOURCE = "Hints.cfg";
		private const string CLASS_MARKER = "class ";
		private const string GROUP_MARKER = "group ";
		private const string DEFAULT_HINT = "{\\rtf1 Press {\\b F1} to show help for current editing mode}";
		internal const int MODIFIER_MASK = (int)(Keys.Control | Keys.Shift | Keys.Alt);
		internal const int MAX_OVERLAY_SHORTCUTS = 12;

		#endregion

		#region ================== Variables

		private readonly Dictionary<string, Dictionary<string, List<string>>> hints; //<asm_classname, <group, raw hint lines>>

		// Current state
		private Type currenttype;
		private string currentgroup;
		private int overlaymask;

		// Injected so tests can run without General.Interface and General.Actions
		private readonly System.Action<string> output;
		private readonly Func<string, Action> findaction;
		private readonly Func<IEnumerable<Action>> allactions;
		private readonly Func<object> currentmode;

		#endregion

		#region ================== Properties

		internal int OverlayMask { get { return overlaymask; } }

		#endregion

		#region ================== Constructor

		public HintsManager() : this(
			rtf => General.Interface.ShowHints(rtf),
			name => General.Actions.Exists(name) ? General.Actions.GetActionByName(name) : null,
			() => General.Actions.GetAllActions(),
			() => General.Editing != null ? General.Editing.Mode : null)
		{
		}

		internal HintsManager(System.Action<string> output, Func<string, Action> findaction, Func<IEnumerable<Action>> allactions, Func<object> currentmode)
		{
			hints = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
			this.output = output;
			this.findaction = findaction;
			this.allactions = allactions;
			this.currentmode = currentmode;
		}

		#endregion

		#region ================== Loading

		//Hints.cfg is dev-only stuff so bare minimum of boilerplate is present
		//(e.g. create your Hints.cfg exactly the way it's done in the main project).
		internal void LoadHints(Assembly asm)
		{
			// Find a resource named Hints.cfg
			string[] resnames = asm.GetManifestResourceNames();

			foreach(string rn in resnames)
			{
				// Found one?
				if(rn.EndsWith(HINTS_RESOURCE, StringComparison.OrdinalIgnoreCase))
				{
					Stream data = asm.GetManifestResourceStream(rn);
					if(data == null) return;
					using(StreamReader reader = new StreamReader(data, Encoding.ASCII))
					{
						AddHints(ParseHints(reader, AssemblyPrefix(asm)));
					}
					return;
				}
			}
		}

		internal void AddHints(Dictionary<string, Dictionary<string, List<string>>> parsed)
		{
			foreach(KeyValuePair<string, Dictionary<string, List<string>>> pair in parsed)
			{
				if(hints.ContainsKey(pair.Key)) throw new InvalidDataException("Hints.cfg: class \"" + pair.Key + "\" is declared twice");
				hints.Add(pair.Key, pair.Value);
			}
		}

		// Returns <asmprefix + classname, <group, raw lines>>. Quotes are stripped; tags stay unresolved.
		internal static Dictionary<string, Dictionary<string, List<string>>> ParseHints(TextReader reader, string asmprefix)
		{
			var result = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
			Dictionary<string, List<string>> groups = null;
			List<string> lines = null;
			string classname = null;

			string s;
			while((s = reader.ReadLine()) != null)
			{
				string line = s.Trim();
				if(line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

				if(line.StartsWith(CLASS_MARKER, StringComparison.Ordinal))
				{
					classname = line.Substring(CLASS_MARKER.Length).Trim();
					if(result.ContainsKey(asmprefix + classname)) throw new InvalidDataException("Hints.cfg: class \"" + classname + "\" is declared twice");
					groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
					result.Add(asmprefix + classname, groups);
					lines = null;
					continue;
				}

				if(line.StartsWith(GROUP_MARKER, StringComparison.Ordinal))
				{
					string groupname = line.Substring(GROUP_MARKER.Length).Trim();
					if(groups == null) throw new InvalidDataException("Hints.cfg: group \"" + groupname + "\" is outside of a class");
					if(groups.ContainsKey(groupname)) throw new InvalidDataException("Hints.cfg: group \"" + groupname + "\" is declared twice in class \"" + classname + "\"");
					lines = new List<string>();
					groups.Add(groupname, lines);
					continue;
				}

				if(lines == null) throw new InvalidDataException("Hints.cfg: hint line outside of a group in class \"" + classname + "\"");
				lines.Add(line.Replace("\"", string.Empty));
			}

			return result;
		}

		private static string AssemblyPrefix(Assembly asm)
		{
			return asm.GetName().Name.ToLowerInvariant() + "_";
		}

		#endregion

		#region ================== Showing

		public void ShowHints(Type type, string groupname)
		{
			currenttype = type;
			currentgroup = groupname;
			Render();
		}

		// Re-renders the current hints, e.g. after shortcut keys changed
		public void Refresh()
		{
			if(currenttype != null) Render();
		}

		// Shows what the held modifiers do (Keys.Control/Shift/Alt bits). 0 restores the normal hints.
		internal void SetModifierOverlay(int mask)
		{
			mask &= MODIFIER_MASK;
			if(mask == overlaymask) return;
			overlaymask = mask;
			if(currenttype != null) Render();
		}

		private void Render()
		{
			if(overlaymask != 0)
			{
				string overlay = RenderOverlay(overlaymask, FindGroup(currenttype, ModifierGroupName(overlaymask)),
					BuildShortcutList(allactions(), overlaymask, currentmode()), DescribeAction);
				if(overlay != null)
				{
					output(overlay);
					return;
				}
			}

			List<string> lines = FindGroup(currenttype, currentgroup);
			if(lines == null)
			{
#if DEBUG
				Console.WriteLine("WARNING: Unable to get hints for class \"" + currenttype.Name + "\", group \"" + currentgroup + "\"");
#endif
				output(DEFAULT_HINT);
				return;
			}

			output(RenderGroup(lines, DescribeAction));
		}

		// Looks the group up on the type, then on its base types (each in its own assembly)
		private List<string> FindGroup(Type type, string groupname)
		{
			for(Type t = type; t != null; t = t.BaseType)
			{
				Dictionary<string, List<string>> groups;
				List<string> lines;
				if(hints.TryGetValue(AssemblyPrefix(t.Assembly) + t.Name, out groups) && groups.TryGetValue(groupname, out lines))
					return lines;
			}
			return null;
		}

		private string DescribeAction(string name)
		{
			Action a = findaction(name);
			return a == null ? name : DescribeKeys(a);
		}

		#endregion

		#region ================== Rendering

		internal static string ModifierGroupName(int mask)
		{
			StringBuilder sb = new StringBuilder("mod");
			if((mask & (int)Keys.Control) != 0) sb.Append("_ctrl");
			if((mask & (int)Keys.Alt) != 0) sb.Append("_alt");
			if((mask & (int)Keys.Shift) != 0) sb.Append("_shift");
			return sb.ToString();
		}

		internal static string ModifierChordName(int mask)
		{
			List<string> parts = new List<string>(3);
			if((mask & (int)Keys.Control) != 0) parts.Add("Ctrl");
			if((mask & (int)Keys.Alt) != 0) parts.Add("Alt");
			if((mask & (int)Keys.Shift) != 0) parts.Add("Shift");
			return string.Join("+", parts.ToArray());
		}

		// Both keys when an alternate is set; unbound when neither key is > 0
		internal static string DescribeKeys(Action a)
		{
			List<string> keys = new List<string>(2);
			if(a.ShortcutKey > 0) keys.Add(Action.GetShortcutKeyDesc(a.ShortcutKey));
			if(a.AlternateShortcutKey > 0) keys.Add(Action.GetShortcutKeyDesc(a.AlternateShortcutKey));
			if(keys.Count == 0) return a.Title + " (not bound to a key)";
			return string.Join(" or ", keys.ToArray());
		}

		internal static string RenderGroup(IList<string> lines, Func<string, string> describeaction)
		{
			List<string> rendered = new List<string>(lines.Count);
			foreach(string line in lines) rendered.Add(RenderLine(line, describeaction));
			return "{\\rtf1 " + string.Join("\\par\\par ", rendered.ToArray()) + "}";
		}

		// Turns <b>, </b>, <br> and <k>action</k> into RTF and escapes everything else
		internal static string RenderLine(string line, Func<string, string> describeaction)
		{
			StringBuilder sb = new StringBuilder(line.Length + 16);
			int pos = 0;
			while(pos < line.Length)
			{
				if(string.CompareOrdinal(line, pos, "<b>", 0, 3) == 0) { sb.Append("{\\b "); pos += 3; continue; }
				if(string.CompareOrdinal(line, pos, "</b>", 0, 4) == 0) { sb.Append("}"); pos += 4; continue; }
				if(string.CompareOrdinal(line, pos, "<br>", 0, 4) == 0) { sb.Append("\\par "); pos += 4; continue; }
				if(string.CompareOrdinal(line, pos, "<k>", 0, 3) == 0)
				{
					int end = line.IndexOf("</k>", pos + 3, StringComparison.Ordinal);
					if(end != -1)
					{
						string name = line.Substring(pos + 3, end - pos - 3);
						sb.Append("{\\b ").Append(EscapeRtf(describeaction(name))).Append("}");
						pos = end + 4;
						continue;
					}
				}
				AppendEscaped(sb, line[pos]);
				pos++;
			}
			return sb.ToString();
		}

		internal static string EscapeRtf(string text)
		{
			StringBuilder sb = new StringBuilder(text.Length);
			foreach(char c in text) AppendEscaped(sb, c);
			return sb.ToString();
		}

		private static void AppendEscaped(StringBuilder sb, char c)
		{
			if(c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
			else if(c > 127) sb.Append("\\u").Append((int)(short)c).Append('?');
			else sb.Append(c);
		}

		// Bound actions whose primary or alternate key carries exactly these modifiers, as <key text, title>.
		// Actions bound to the current mode come first; each part is sorted by key text.
		internal static List<KeyValuePair<string, string>> BuildShortcutList(IEnumerable<Action> actions, int mask, object mode)
		{
			var modeitems = new List<KeyValuePair<string, string>>();
			var otheritems = new List<KeyValuePair<string, string>>();
			foreach(Action a in actions)
			{
				if(!a.BeginBound) continue;
				List<string> keys = new List<string>(2);
				if(KeyHasExactModifiers(a.ShortcutKey, mask)) keys.Add(Action.GetShortcutKeyDesc(a.ShortcutKey));
				if(KeyHasExactModifiers(a.AlternateShortcutKey, mask)) keys.Add(Action.GetShortcutKeyDesc(a.AlternateShortcutKey));
				if(keys.Count == 0) continue;
				var item = new KeyValuePair<string, string>(string.Join(" or ", keys.ToArray()), a.Title);
				(a.IsBeginBoundTo(mode) ? modeitems : otheritems).Add(item);
			}
			Comparison<KeyValuePair<string, string>> bykey = (x, y) =>
			{
				int c = string.Compare(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);
				return c != 0 ? c : string.Compare(x.Value, y.Value, StringComparison.OrdinalIgnoreCase);
			};
			modeitems.Sort(bykey);
			otheritems.Sort(bykey);
			modeitems.AddRange(otheritems);
			return modeitems;
		}

		internal static bool KeyHasExactModifiers(int key, int mask)
		{
			if(key <= 0 || (key & MODIFIER_MASK) != mask) return false;
			int button = key & ~MODIFIER_MASK;
			return button != 0 && !IsModifierKey((Keys)button);
		}

		internal static bool IsModifierKey(Keys keycode)
		{
			switch(keycode)
			{
				case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
				case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
				case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
					return true;
				default:
					return false;
			}
		}

		// Returns null when there is nothing to show for this chord
		internal static string RenderOverlay(int mask, IList<string> modelines, IList<KeyValuePair<string, string>> shortcuts, Func<string, string> describeaction)
		{
			bool hasmodelines = modelines != null && modelines.Count > 0;
			if(!hasmodelines && shortcuts.Count == 0) return null;

			string chord = ModifierChordName(mask);
			StringBuilder sb = new StringBuilder("{\\rtf1 {\\b ").Append(EscapeRtf(chord)).Append(" held}");
			if(hasmodelines)
			{
				foreach(string line in modelines) sb.Append("\\par\\par ").Append(RenderLine(line, describeaction));
			}
			if(shortcuts.Count > 0)
			{
				sb.Append("\\par\\par {\\b Shortcuts with ").Append(EscapeRtf(chord)).Append(":}");
				int shown = Math.Min(shortcuts.Count, MAX_OVERLAY_SHORTCUTS);
				for(int i = 0; i < shown; i++)
					sb.Append("\\par {\\b ").Append(EscapeRtf(shortcuts[i].Key)).Append("}  ").Append(EscapeRtf(shortcuts[i].Value));
				if(shortcuts.Count > shown)
					sb.Append("\\par ... and ").Append(shortcuts.Count - shown).Append(" more");
			}
			return sb.Append("}").ToString();
		}

		#endregion

		#region ================== Utility

		public static string GetRtfString(string text)
		{
			text = text.Replace("<b>", "{\\b ").Replace("</b>", "}").Replace("<br>", "\\par\\par ");
			return "{\\rtf1" + text + "}";
		}

		#endregion
	}
}
