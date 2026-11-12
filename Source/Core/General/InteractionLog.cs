#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder
{
	// Opt-in log of user interactions with their timings (UDB_INTERACTION_LOG=1 or the
	// "interactionlog" setting), one tab-separated line per event. It never records map
	// content beyond counts, and a logging failure only disables the log.
	internal static class InteractionLog
	{
		#region ================== Constants

		internal const int FORMAT = 1;
		internal const string FILE_NAME = "UDBuilder.interaction.log";
		internal const string SETTING = "interactionlog";
		private const int ROTATE_CHECK_LINES = 2000;
		private const int HEARTBEAT_MS = 100;
		private const double STALL_MS = 250.0;
		private const double SUSPECT_STALL_MS = 30000.0;
		private const double MODE_STAMP_MAX_MS = 60000.0;
		private const long LOCK_OFFSET = 1L << 40; // far past any real size, so readers are never blocked

		#endregion

		#region ================== Variables

		internal static long MaxBytes = 20L * 1024 * 1024;

		private static readonly object writelock = new object();
		private static bool enabled;
		private static StreamWriter writer;
		private static string logpath;
		private static int linessincecheck;
		private static long mainentry = Stopwatch.GetTimestamp();
		private static InteractionActionTracker tracker;

		private static Timer heartbeat;
		private static double lasttick = -1;
		private static bool focusstate;
		private static int focuspending;
		private static double resourcesstart = -1;
		private static string resourceswhy;
		private static double modestamp = -1;
		private static Type modestamptype;
		private static int dialogcounter;
		private static int markcounter;
		private static bool withdrawing;

		#endregion

		#region ================== Properties

		internal static bool Enabled { get { return enabled; } }
		internal static InteractionActionTracker Tracker { get { return tracker; } }
		internal static double Now { get { return (Stopwatch.GetTimestamp() - mainentry) * 1000.0 / Stopwatch.Frequency; } }

		#endregion

		#region ================== Lifecycle

		// Called first thing in Main, so startup and every t= are measured from process start
		internal static void MarkMainEntry()
		{
			mainentry = Stopwatch.GetTimestamp();
		}

		internal static bool IsRequested(bool setting)
		{
			try { return setting || Environment.GetEnvironmentVariable("UDB_INTERACTION_LOG") == "1"; }
			catch(Exception) { return false; }
		}

		internal static void Start(bool setting)
		{
			if(!IsRequested(setting)) return;
			try
			{
				if(!Open(Path.Combine(General.SettingsPath, FILE_NAME))) return;
				Rectangle screen = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.Bounds : Rectangle.Empty;
				Write("session", "fmt", FORMAT.ToString(CultureInfo.InvariantCulture),
					"wall", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
					"udb", "R" + General.ThisAssembly.GetName().Version.Revision + " " + General.CommitHash,
					"mono", MonoVersion(), "screen", screen.Width + "x" + screen.Height,
					"pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
			}
			catch(Exception) { Disable(); }
		}

		// Separate from Start so tests can point the log at a scratch file
		internal static bool Open(string path)
		{
			lock(writelock)
			{
				try
				{
					CloseWriter();
					logpath = path;
					// Lock before rotating, so a second editor instance can't move the first one's file
					OpenWriter(path);
					if(writer.BaseStream.Length >= MaxBytes) RotateLocked();
					linessincecheck = 0;
					tracker = new InteractionActionTracker(() => Now, Write);
					enabled = true;
					return true;
				}
				catch(Exception)
				{
					DisableLocked();
					return false;
				}
			}
		}

		internal static void Stop()
		{
			if(!enabled) return;
			StopHeartbeat();
			Write("end");
			lock(writelock) DisableLocked();
		}

		private static void Disable()
		{
			lock(writelock) DisableLocked();
		}

		private static void DisableLocked()
		{
			enabled = false;
			CloseWriter();
		}

		private static void CloseWriter()
		{
			try { if(writer != null) writer.Dispose(); }
			catch(Exception) { }
			writer = null;
		}

		// One writer per file: a second instance's lines would interleave and reuse seq numbers
		private static void OpenWriter(string path)
		{
			FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
			try { fs.Lock(LOCK_OFFSET, 1); }
			catch(IOException)
			{
				fs.Dispose();
				try { General.WriteLogLine("Interaction log is in use by another instance; not logging this one."); }
				catch(Exception) { }
				throw;
			}
			writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
		}

		private static void RotateLocked()
		{
			CloseWriter();
			string previous = logpath + ".1";
			if(File.Exists(previous)) File.Delete(previous);
			File.Move(logpath, previous);
			OpenWriter(logpath);
		}

		private static string MonoVersion()
		{
			try
			{
				Type mono = Type.GetType("Mono.Runtime");
				if(mono == null) return Environment.Version.ToString();
				object name = mono.GetMethod("GetDisplayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
				return name != null ? name.ToString() : "?";
			}
			catch(Exception) { return "?"; }
		}

		#endregion

		#region ================== Writing

		internal static void Write(string ev, params string[] fields)
		{
			WriteAt(Now, ev, fields);
		}

		internal static void WriteAt(double t, string ev, params string[] fields)
		{
			if(!enabled) return;
			lock(writelock)
			{
				if(writer == null) return;
				try
				{
					writer.WriteLine(FormatLine(t, ev, fields));
					if(++linessincecheck >= ROTATE_CHECK_LINES)
					{
						linessincecheck = 0;
						if(writer.BaseStream.Length >= MaxBytes) RotateLocked();
					}
				}
				catch(Exception) { DisableLocked(); }
			}
		}

		// fields alternate key and value
		internal static string FormatLine(double t, string ev, string[] fields)
		{
			StringBuilder sb = new StringBuilder(64);
			sb.Append("t=").Append(t.ToString("0.0", CultureInfo.InvariantCulture));
			sb.Append("\tev=").Append(Sanitize(ev));
			if(fields != null)
			{
				for(int i = 0; i + 1 < fields.Length; i += 2)
					sb.Append('\t').Append(Sanitize(fields[i])).Append('=').Append(Sanitize(fields[i + 1]));
			}
			return sb.ToString();
		}

		internal static string Sanitize(string value)
		{
			if(string.IsNullOrEmpty(value)) return "";
			return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
		}

		// Built on first use, so a run with the log off never compiles them
		private static class Patterns
		{
			internal static readonly Regex DigitRuns = new Regex("[0-9]+");
			internal static readonly Regex FirstWord = new Regex("^(N|[A-Za-z][a-z]*(-[a-z]+)*)$");
			internal static readonly Regex LaterWord = new Regex("^(N|[a-z]+(-[a-z]+)*)$");
		}

		// Undo descriptions embed texture and other names: keep only the leading plain words
		internal static string ReduceDescription(string desc)
		{
			if(string.IsNullOrEmpty(desc)) return "";
			string[] words = Patterns.DigitRuns.Replace(desc, "N").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			List<string> kept = new List<string>(4);
			foreach(string w in words)
			{
				if(kept.Count == 4 || !(kept.Count == 0 ? Patterns.FirstWord : Patterns.LaterWord).IsMatch(w)) break;
				kept.Add(w);
			}
			return string.Join(" ", kept.ToArray());
		}

		internal static string Ms(double ms)
		{
			return InteractionActionTracker.Ms(ms);
		}

		private static string Num(int n)
		{
			return n.ToString(CultureInfo.InvariantCulture);
		}

		#endregion

		#region ================== Actions

		internal static void KeyTrigger(object action, int key)
		{
			if(!enabled) return;
			int code = key & (int)Keys.KeyCode;
			string kind;
			if(code == (int)Keys.LButton || code == (int)Keys.RButton || code == (int)Keys.MButton
			   || code == (int)Keys.XButton1 || code == (int)Keys.XButton2) kind = "mouse";
			else if(code >= (int)Actions.SpecialKeys.MScrollUp && code <= (int)Actions.SpecialKeys.MScrollRight) kind = "wheel";
			else kind = "key";
			string desc;
			try { desc = Actions.Action.GetShortcutKeyDesc(key); }
			catch(Exception) { desc = ""; }
			tracker.SetTrigger(action, kind, desc);
		}

		// menu, toolbar, status or context, from where a clicked ToolStripItem lives
		internal static string TriggerFor(object sender)
		{
			ToolStripItem item = sender as ToolStripItem;
			if(item == null) return sender is Control ? "control" : "code";
			ToolStripItem root = item;
			while(root.OwnerItem != null) root = root.OwnerItem;
			ToolStrip owner = root.Owner;
			if(owner is MenuStrip) return "menu";
			if(owner is ContextMenuStrip) return "context";
			if(owner is StatusStrip) return "status";
			if(owner is ToolStrip) return "toolbar";
			return "code";
		}

		internal static string BeginAmbient(string kind)
		{
			return enabled ? tracker.SetAmbient(kind) : null;
		}

		internal static void EndAmbient(string previous)
		{
			if(enabled) tracker.RestoreAmbient(previous);
		}

		#endregion

		#region ================== Modes, undo, maps

		internal static void ModeConstructing(Type type)
		{
			modestamp = Now;
			modestamptype = type;
		}

		// A stamp counts only for the type it was taken for, so a failed construction can't leak into another switch
		internal static double TakeModeStart(object nextmode)
		{
			double now = Now;
			bool match = modestamp >= 0 && nextmode != null && nextmode.GetType() == modestamptype && now - modestamp < MODE_STAMP_MAX_MS;
			double start = match ? modestamp : now;
			modestamp = -1;
			modestamptype = null;
			return start;
		}

		internal static void ModeChanged(object from, object to, double start, bool ok)
		{
			if(!enabled) return;
			Write("mode", "from", from != null ? from.GetType().Name : "none", "to", to != null ? to.GetType().Name : "none",
				"ms", Ms(Now - start), "ok", ok ? "1" : "0");
		}

		internal static void UndoCreated(string description, bool grouped)
		{
			if(!enabled) return;
			Actions.Action current = General.Actions != null ? General.Actions.Current : null;
			Write("undo-create", "desc", ReduceDescription(description), "action", current != null ? current.Name : "-",
				"group", grouped ? "1" : "0");
		}

		internal static void BeginWithdraw() { withdrawing = true; }
		internal static void EndWithdraw() { withdrawing = false; }

		internal static void UndoPerformed(bool redo, int levels, int done, bool quiet, double start)
		{
			if(!enabled) return;
			string ev = redo ? "redo" : (withdrawing ? "withdraw" : "undo");
			List<string> f = new List<string> { "levels", Num(levels), "done", Num(done), "ms", Ms(Now - start) };
			if(quiet && !withdrawing) { f.Add("quiet"); f.Add("1"); }
			Write(ev, f.ToArray());
		}

		internal static void MapEvent(string ev, MapManager manager, double start, bool ok, string purpose)
		{
			if(!enabled) return;
			try
			{
				List<string> f = new List<string> { "ms", Ms(Now - start), "ok", ok ? "1" : "0" };
				if(purpose != null) { f.Add("purpose"); f.Add(purpose); }
				if(manager != null && manager.Config != null) { f.Add("format"); f.Add(manager.Config.FormatInterface); }
				if(manager != null && manager.Map != null)
				{
					f.AddRange(new[] { "v", Num(manager.Map.Vertices.Count), "l", Num(manager.Map.Linedefs.Count),
						"s", Num(manager.Map.Sectors.Count), "t", Num(manager.Map.Things.Count) });
				}
				Write(ev, f.ToArray());
			}
			catch(Exception) { }
		}

		// The end is the heartbeat's first tick with background loading finished
		internal static void ResourcesStarting(string why)
		{
			resourcesstart = Now;
			resourceswhy = why;
		}

		internal static void ResourcesCancelled()
		{
			resourcesstart = -1;
		}

		// why is null for operations that load no resources
		internal static bool TimeMap(string ev, string why, MapManager manager, string purpose, Func<bool> op)
		{
			double start = Now;
			if(why != null) ResourcesStarting(why);
			bool ok = false;
			try { ok = op(); }
			finally
			{
				if(!ok && why != null) ResourcesCancelled();
				MapEvent(ev, manager, start, ok, purpose);
			}
			return ok;
		}

		#endregion

		#region ================== Heartbeat

		internal static void StartHeartbeat()
		{
			if(!enabled || heartbeat != null) return;
			try
			{
				focusstate = Form.ActiveForm != null;
				Write("focus", "state", focusstate ? "on" : "off");
				lasttick = Now;
				heartbeat = new GuardedTimer { Interval = HEARTBEAT_MS };
				heartbeat.Tick += OnHeartbeat;
				heartbeat.Start();
			}
			catch(Exception) { }
		}

		private static void StopHeartbeat()
		{
			try
			{
				if(heartbeat != null)
				{
					heartbeat.Stop();
					heartbeat.Dispose();
				}
			}
			catch(Exception) { }
			heartbeat = null;
		}

		private static void OnHeartbeat(object sender, EventArgs e)
		{
			if(!enabled) { StopHeartbeat(); return; }
			try
			{
				double now = Now;
				double gap = now - lasttick;
				if(gap > STALL_MS)
				{
					if(gap > SUSPECT_STALL_MS) WriteAt(lasttick, "stall", "gap_ms", Ms(gap), "suspect", "1");
					else WriteAt(lasttick, "stall", "gap_ms", Ms(gap));
				}
				lasttick = now;

				// A UDB dialog taking activation from the main window can leave one tick with no active form
				bool active = Form.ActiveForm != null;
				if(active == focusstate) focuspending = 0;
				else if(++focuspending >= 2)
				{
					focusstate = active;
					focuspending = 0;
					WriteAt(now - HEARTBEAT_MS, "focus", "state", active ? "on" : "off");
				}

				if(resourcesstart >= 0 && General.Map != null && General.Map.Data != null && !General.Map.Data.IsLoading)
				{
					Write("resources", "ms", Ms(now - resourcesstart), "why", resourceswhy);
					resourcesstart = -1;
				}
			}
			catch(Exception) { }
		}

		#endregion

		#region ================== Dialogs

		internal sealed class DialogState
		{
			internal int Id;
			internal string Form;
			internal double Created;
			internal double ShownAt = -1;
			internal bool Visible;
			internal bool EverShown;
		}

		internal static DialogState DialogCreated(Form form)
		{
			if(!enabled) return null;
			return new DialogState { Id = ++dialogcounter, Form = form.GetType().Name, Created = Now };
		}

		internal static void DialogShown(DialogState s, Form form, bool reshow)
		{
			if(!enabled || s == null || s.Visible) return;
			if(reshow && !s.EverShown) return;
			double now = Now;
			s.Visible = true;
			s.ShownAt = now;
			List<string> f = new List<string> { "form", s.Form, "id", Num(s.Id), "modal", form.Modal ? "1" : "0" };
			if(reshow) { f.Add("reshow"); f.Add("1"); }
			else { f.Add("open_ms"); f.Add(Ms(now - s.Created)); }
			s.EverShown = true;
			Write("dialog-open", f.ToArray());
		}

		internal static void DialogHidden(DialogState s, DelayedForm form)
		{
			if(!enabled || s == null || !s.Visible) return;
			s.Visible = false;
			string extra = null;
			try { extra = form.InteractionLogExtra; }
			catch(Exception) { }
			List<string> f = new List<string> { "form", s.Form, "id", Num(s.Id), "dwell_ms", Ms(Now - s.ShownAt),
				"result", form.DialogResult.ToString() };
			if(!string.IsNullOrEmpty(extra)) { f.Add("extra"); f.Add(extra); }
			Write("dialog-close", f.ToArray());
		}

		#endregion

		#region ================== Marks

		internal static void Mark()
		{
			if(!enabled) return;
			int id = ++markcounter;
			Write("mark", "id", Num(id));
			string note = "";
			try { note = AskNote(); }
			catch(Exception) { }
			Write("mark-note", "id", Num(id), "note", note);
		}

		private static string AskNote()
		{
			using(Form form = new Form())
			{
				form.Text = "Interaction log mark";
				form.FormBorderStyle = FormBorderStyle.FixedDialog;
				form.StartPosition = FormStartPosition.CenterParent;
				form.ShowInTaskbar = false;
				form.MinimizeBox = false;
				form.MaximizeBox = false;
				form.ClientSize = new Size(420, 64);

				Label label = new Label { Text = "What felt slow or tedious? (Enter saves, Esc skips)", AutoSize = true, Location = new Point(8, 8) };
				TextBox box = new TextBox { Location = new Point(8, 30), Width = 404 };
				Button ok = new Button { DialogResult = DialogResult.OK, Location = new Point(-100, -100) };
				Button cancel = new Button { DialogResult = DialogResult.Cancel, Location = new Point(-100, -100) };
				form.Controls.AddRange(new Control[] { label, box, ok, cancel });
				form.AcceptButton = ok;
				form.CancelButton = cancel;

				IWin32Window owner = General.MainWindow;
				DialogResult result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
				return result == DialogResult.OK ? box.Text : "";
			}
		}

		#endregion
	}
}
