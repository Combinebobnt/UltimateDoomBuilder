#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Shows file and folder pickers through the desktop's GTK chooser (zenity) on Mono, else the stock dialog.
	public static class NativeFileDialogs
	{
		public static DialogResult Show(FileDialog dialog, IWin32Window owner = null)
		{
#if MONO_WINFORMS
			if(NativeEnabled)
			{
				DialogSpec spec = new DialogSpec
				{
					Title = dialog.Title,
					Filter = dialog.Filter,
					FilterIndex = dialog.FilterIndex,
					InitialDirectory = dialog.InitialDirectory,
					FileName = dialog.FileName,
					Save = (dialog is SaveFileDialog),
					OverwritePrompt = (dialog is SaveFileDialog) && ((SaveFileDialog)dialog).OverwritePrompt,
					AddExtension = dialog.AddExtension,
					DefaultExt = dialog.DefaultExt,
				};

				string path;
				DialogResult result = RunFileDialog(spec, owner, out path);
				if(result != DialogResult.None)
				{
					if(result == DialogResult.OK) dialog.FileName = path;
					return result;
				}
			}
#endif
			return (owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner));
		}

		public static DialogResult Show(FolderBrowserDialog dialog, IWin32Window owner = null)
		{
#if MONO_WINFORMS
			if(NativeEnabled)
			{
				DialogSpec spec = new DialogSpec { Title = dialog.Description, InitialDirectory = dialog.SelectedPath, Folder = true };
				ZenityResult zr = ZenityBackend.Run(spec, owner);
				if(zr.Outcome == ZenityOutcome.Ok) { dialog.SelectedPath = zr.Path; return DialogResult.OK; }
				if(zr.Outcome == ZenityOutcome.Cancel) return DialogResult.Cancel;
			}
#endif
			return (owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner));
		}

#if MONO_WINFORMS
		private static bool NativeEnabled { get { return (General.Settings == null || General.Settings.NativeFileDialogs); } }

		// DialogResult.None means the native backend is unavailable and the caller falls back
		private static DialogResult RunFileDialog(DialogSpec spec, IWin32Window owner, out string path)
		{
			path = null;
			while(true)
			{
				ZenityResult zr = ZenityBackend.Run(spec, owner);
				if(zr.Outcome == ZenityOutcome.Unavailable) return DialogResult.None;
				if(zr.Outcome == ZenityOutcome.Cancel) return DialogResult.Cancel;

				path = ZenityArgs.ApplyDefaultExtension(zr.Path, spec);
				if(spec.Save && spec.OverwritePrompt && File.Exists(path))
				{
					string msg = Path.GetFileName(path) + " already exists.\nDo you want to replace it?";
					string title = (string.IsNullOrEmpty(spec.Title) ? Application.ProductName : spec.Title);
					if(MessageBox.Show(ZenityBackend.ResolveOwner(owner), msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
					{
						// Reopen the chooser at the rejected path
						spec.InitialDirectory = Path.GetDirectoryName(path);
						spec.FileName = Path.GetFileName(path);
						continue;
					}
				}
				return DialogResult.OK;
			}
		}
#endif
	}

	// Plain copy of the dialog properties the zenity argument builder needs
	internal class DialogSpec
	{
		public string Title;
		public string Filter;
		public int FilterIndex = 1;
		public string InitialDirectory;
		public string FileName;
		public bool Save;
		public bool Folder;
		public bool OverwritePrompt;
		public bool AddExtension;
		public string DefaultExt;
		public long AttachWindow;
	}

	internal enum ZenityOutcome { Ok, Cancel, Unavailable }

	internal struct ZenityResult
	{
		public ZenityOutcome Outcome;
		public string Path;
		public ZenityResult(ZenityOutcome outcome, string path) { Outcome = outcome; Path = path; }
	}

	// Drops queued keyboard and mouse messages while an external chooser runs; paint, timers and the rest pass
	internal class InputBlocker : IMessageFilter
	{
		public bool PreFilterMessage(ref Message m)
		{
			return IsInput(m.Msg);
		}

		// WM_NCMOUSEMOVE..WM_NCXBUTTONDBLCLK, WM_KEYDOWN..WM_UNICHAR, WM_MOUSEMOVE..WM_MOUSEHWHEEL
		public static bool IsInput(int msg)
		{
			return (msg >= 0x00A0 && msg <= 0x00AD) || (msg >= 0x0100 && msg <= 0x0109) || (msg >= 0x0200 && msg <= 0x020E);
		}
	}

	// Pure helpers: zenity argv, filter translation, quoting and result classification
	internal static class ZenityArgs
	{
		public static List<string> Build(DialogSpec spec)
		{
			List<string> args = new List<string> { "--file-selection" };
			if(spec.Folder) args.Add("--directory");
			else if(spec.Save) args.Add("--save");
			if(!string.IsNullOrEmpty(spec.Title)) args.Add("--title=" + spec.Title);

			string start = StartLocation(spec.InitialDirectory, spec.FileName);
			if(start != null) args.Add("--filename=" + start);

			if(!spec.Folder) args.AddRange(FilterArgs(spec.Filter, spec.FilterIndex));
			// zenity ignores --attach for choosers; UDB sets the transient hint itself. No --modal without an owner, since some WMs make that group-modal
			if(spec.AttachWindow > 0) { args.Add("--attach=" + spec.AttachWindow); args.Add("--modal"); }
			return args;
		}

		// The chooser among _NET_CLIENT_LIST: the first window of zenity's pid not yet transient (GTK's own sub-dialogs are), else 0
		public static long PickChooser(long[] windows, long[] pids, bool[] hastransient, long pid)
		{
			for(int i = 0; i < windows.Length; i++)
			{
				if(pids[i] == pid && !hastransient[i]) return windows[i];
			}
			return 0;
		}

		// "<dir>/<name>", "<dir>/" (zenity opens the folder), a rooted name as-is, or null for no start location
		public static string StartLocation(string dir, string name)
		{
			dir = Normalize(dir);
			name = Normalize(name);
			if(!string.IsNullOrEmpty(name) && name.StartsWith("/")) return name;
			if(string.IsNullOrEmpty(dir)) return (string.IsNullOrEmpty(name) ? null : name);
			if(!dir.EndsWith("/")) dir += "/";
			return dir + (name ?? "");
		}

		private static string Normalize(string path)
		{
			return (string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/'));
		}

		// WinForms filter pairs in display order, the FilterIndex pair first; null on a malformed filter
		public static List<KeyValuePair<string, List<string>>> ParseFilter(string filter, int filterindex)
		{
			List<KeyValuePair<string, List<string>>> pairs = new List<KeyValuePair<string, List<string>>>();
			if(string.IsNullOrEmpty(filter)) return pairs;
			string[] parts = filter.Split('|');
			if(parts.Length % 2 != 0) return null;

			for(int i = 0; i < parts.Length; i += 2)
			{
				List<string> patterns = new List<string>();
				foreach(string p in parts[i + 1].Split(';'))
				{
					string pt = p.Trim();
					if(pt.Length > 0) patterns.Add(pt == "*.*" ? "*" : pt);
				}
				pairs.Add(new KeyValuePair<string, List<string>>(parts[i].Trim(), patterns));
			}

			int index = Math.Max(1, Math.Min(filterindex, pairs.Count)) - 1;
			if(index > 0)
			{
				KeyValuePair<string, List<string>> first = pairs[index];
				pairs.RemoveAt(index);
				pairs.Insert(0, first);
			}
			return pairs;
		}

		public static List<string> FilterArgs(string filter, int filterindex)
		{
			List<string> args = new List<string>();
			List<KeyValuePair<string, List<string>>> pairs = ParseFilter(filter, filterindex);
			if(pairs == null) return args;
			foreach(KeyValuePair<string, List<string>> pair in pairs)
			{
				if(pair.Value.Count == 0) continue;
				args.Add("--file-filter=" + pair.Key + " | " + string.Join(" ", pair.Value));
			}
			return args;
		}

		// Save only: appends DefaultExt, else the first concrete extension of the first-listed filter
		public static string ApplyDefaultExtension(string path, DialogSpec spec)
		{
			if(!spec.Save || !spec.AddExtension || string.IsNullOrEmpty(path)) return path;
			if(Path.HasExtension(Path.GetFileName(path))) return path;

			string ext = (spec.DefaultExt ?? "").Trim().TrimStart('.');
			if(ext.Length == 0)
			{
				List<KeyValuePair<string, List<string>>> pairs = ParseFilter(spec.Filter, spec.FilterIndex);
				if(pairs != null && pairs.Count > 0)
				{
					foreach(string p in pairs[0].Value)
					{
						if(!p.StartsWith("*.")) continue;
						string candidate = p.Substring(2);
						if(candidate.Length > 0 && candidate.IndexOfAny(new[] { '*', '?', '[' }) == -1) { ext = candidate; break; }
					}
				}
			}
			return (ext.Length == 0 ? path : path + "." + ext);
		}

		// One argv entry for Mono's Unix process launcher, which splits Arguments with g_shell_parse_argv rules
		public static string Quote(string arg)
		{
			StringBuilder sb = new StringBuilder(arg.Length + 2);
			sb.Append('"');
			foreach(char c in arg)
			{
				if(c == '"' || c == '\\' || c == '$' || c == '`') sb.Append('\\');
				sb.Append(c);
			}
			sb.Append('"');
			return sb.ToString();
		}

		public static string Join(IEnumerable<string> args)
		{
			List<string> quoted = new List<string>();
			foreach(string a in args) quoted.Add(Quote(a));
			return string.Join(" ", quoted);
		}

		// zenity exits 1 both on Cancel and when GTK can't open the display; stderr tells them apart
		public static ZenityResult Classify(int exitcode, string stdout, string stderr)
		{
			if(exitcode == 0)
			{
				string line = (stdout ?? "").Split('\n')[0].TrimEnd('\r');
				return (line.Length > 0 ? new ZenityResult(ZenityOutcome.Ok, line) : new ZenityResult(ZenityOutcome.Cancel, null));
			}
			if(exitcode == 1)
			{
				bool nodisplay = (stderr ?? "").IndexOf("cannot open display", StringComparison.OrdinalIgnoreCase) != -1;
				return new ZenityResult(nodisplay ? ZenityOutcome.Unavailable : ZenityOutcome.Cancel, null);
			}
			return new ZenityResult(ZenityOutcome.Unavailable, null);
		}
	}

#if MONO_WINFORMS
	// Runs zenity as a child process while UDB's windows are disabled or input-blocked
	internal static class ZenityBackend
	{
		private static string zenitypath;
		private static bool searched;
		private static bool failed;
		private static bool hintlogged;

		public static ZenityResult Run(DialogSpec spec, IWin32Window owner)
		{
			string exe = FindZenity();
			if(exe == null || failed) return new ZenityResult(ZenityOutcome.Unavailable, null);
			if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
				return new ZenityResult(ZenityOutcome.Unavailable, null);

			Form ownerform = ResolveOwner(owner);
			spec.AttachWindow = (ownerform != null ? GetXWindow(ownerform) : 0);

			ProcessStartInfo psi = new ProcessStartInfo(exe, ZenityArgs.Join(ZenityArgs.Build(spec)));
			psi.UseShellExecute = false;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;
			psi.CreateNoWindow = true;
			psi.StandardOutputEncoding = Encoding.UTF8;
			psi.StandardErrorEncoding = Encoding.UTF8;

			List<Form> disabled = new List<Form>();
			InputBlocker blocker = null;
			Form blockedmodal = null;
			IntPtr display = IntPtr.Zero;
			ZenityResult result;
			try
			{
				using(Process p = Process.Start(psi))
				{
					var outtask = p.StandardOutput.ReadToEndAsync();
					var errtask = p.StandardError.ReadToEndAsync();

					// Hand-made modality. Under an open modal dialog only that dialog is blocked, since re-enabling
					// a modeless form later would undo Mono's native modal block on it
					List<Form> openforms = new List<Form>(EnumerateForms());
					blockedmodal = TopmostModal(openforms);

					// Keys still reach a focused disabled form, so input is dropped in both branches
					blocker = new InputBlocker();
					Application.AddMessageFilter(blocker);
					if(blockedmodal != null)
					{
						// Mono's Form.WmActivate re-activates the top modal whenever a disabled form gets WM_ACTIVATE,
						// burying the chooser under UDB's dialogs, so that dialog stays enabled with its input dropped
						blockedmodal.FormClosing += CancelClose;
					}
					else
					{
						foreach(Form f in openforms)
						{
							if(!f.Visible || !f.Enabled) continue;
							f.Enabled = false;
							disabled.Add(f);
						}
					}

					// Transient for the blocking modal when there is one, so the chooser stacks above it and not beside it
					Form parentform = (blockedmodal ?? ownerform);
					long parent = (parentform != null ? GetXWindow(parentform) : 0);
					if(parent != 0) display = X11TransientHint.Open();
					bool hintdone = (display == IntPtr.Zero);
					Stopwatch hinttimer = Stopwatch.StartNew();
					int tick = 0;

					while(!p.HasExited)
					{
						Application.DoEvents();
						if(!hintdone && (tick++ % 4) == 0) hintdone = ApplyHint(display, p.Id, parent, hinttimer);
						Thread.Sleep(15);
					}
					p.WaitForExit();

					// A grandchild holding zenity's pipes open must not hang the UI thread
					string stdout = outtask.Wait(2000) ? outtask.Result : string.Empty;
					string stderr = errtask.Wait(2000) ? errtask.Result : string.Empty;
					result = ZenityArgs.Classify(p.ExitCode, stdout, stderr);
					if(result.Outcome == ZenityOutcome.Unavailable) LogUnavailable("exit " + p.ExitCode + ": " + stderr.Trim());
				}
			}
			catch(Exception e)
			{
				LogUnavailable(e.GetType().Name + ": " + e.Message);
				result = new ZenityResult(ZenityOutcome.Unavailable, null);
			}
			finally
			{
				foreach(Form f in disabled) if(!f.IsDisposed) f.Enabled = true;
				if(blocker != null) Application.RemoveMessageFilter(blocker);
				if(blockedmodal != null) blockedmodal.FormClosing -= CancelClose;
				if(display != IntPtr.Zero) X11TransientHint.Close(display);
				if(ownerform != null && !ownerform.IsDisposed) ownerform.Activate();
			}
			return result;
		}

		// True once the hint is set or given up on. An X error here must not escape Run with zenity still open
		private static bool ApplyHint(IntPtr display, int pid, long parent, Stopwatch timer)
		{
			string reason;
			try
			{
				if(X11TransientHint.TryApply(display, pid, parent)) return true;
				if(timer.ElapsedMilliseconds < 10000) return false;
				reason = "no window of zenity's pid in _NET_CLIENT_LIST after 10 s";
			}
			catch(Exception e)
			{
				reason = e.GetType().Name + ": " + e.Message;
			}
			if(!hintlogged) General.WriteLogLine("Native file dialogs: could not make the chooser transient for UDB (" + reason + ").");
			hintlogged = true;
			return true;
		}

		private static IEnumerable<Form> EnumerateForms()
		{
			foreach(Form f in Application.OpenForms) yield return f;
		}

		// OpenForms is in open order, so the last visible modal form is the topmost one
		internal static Form TopmostModal(IList<Form> openforms)
		{
			for(int i = openforms.Count - 1; i >= 0; i--)
			{
				Form f = openforms[i];
				if(f.Modal && f.Visible && f.Enabled) return f;
			}
			return null;
		}

		// The WM close button arrives as a synchronous WM_SYSCOMMAND, which the message filter never sees
		private static void CancelClose(object sender, FormClosingEventArgs e)
		{
			e.Cancel = true;
		}

		internal static Form ResolveOwner(IWin32Window owner)
		{
			Form f = owner as Form;
			if(f == null && owner is Control) f = ((Control)owner).FindForm();
			if(f == null && owner != null && owner.Handle != IntPtr.Zero)
			{
				Control c = Control.FromHandle(owner.Handle);
				if(c != null) f = c.FindForm();
			}
			if(f == null) f = Form.ActiveForm;
			if(f == null) f = General.MainWindow;
			if(f != null && !f.TopLevel && f.TopLevelControl is Form) f = (Form)f.TopLevelControl;
			return f;
		}

		// Mono's Control.Handle is the client window; the WM manages Hwnd.WholeWindow
		private static long GetXWindow(Form form)
		{
			try
			{
				Type hwndtype = typeof(Control).Assembly.GetType("System.Windows.Forms.Hwnd");
				if(hwndtype == null) return 0;
				MethodInfo fromhandle = hwndtype.GetMethod("ObjectFromHandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				PropertyInfo whole = hwndtype.GetProperty("WholeWindow", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if(fromhandle == null || whole == null) return 0;
				object hwnd = fromhandle.Invoke(null, new object[] { form.Handle });
				return (hwnd == null ? 0 : ((IntPtr)whole.GetValue(hwnd, null)).ToInt64());
			}
			catch(Exception)
			{
				return 0;
			}
		}

		private static string FindZenity()
		{
			if(searched) return zenitypath;
			searched = true;
			foreach(string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
			{
				if(dir.Length == 0) continue;
				string candidate = Path.Combine(dir, "zenity");
				if(File.Exists(candidate)) { zenitypath = candidate; break; }
			}
			if(zenitypath == null) General.WriteLogLine("Native file dialogs: zenity not found on PATH, using the Mono dialogs.");
			return zenitypath;
		}

		// Stops further zenity attempts this session
		private static void LogUnavailable(string reason)
		{
			if(!failed) General.WriteLogLine("Native file dialogs: zenity failed (" + reason + "), using the Mono dialogs.");
			failed = true;
		}
	}

	// Sets WM_TRANSIENT_FOR on zenity's chooser from a private X connection, since zenity ignores --attach for it
	internal static class X11TransientHint
	{
		private const string LIBX11 = "libX11";
		private const int XA_CARDINAL = 6;
		private const int XA_WINDOW = 33;

		[DllImport(LIBX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
		[DllImport(LIBX11)] private static extern int XCloseDisplay(IntPtr display);
		[DllImport(LIBX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
		[DllImport(LIBX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyifexists);
		[DllImport(LIBX11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete, IntPtr reqtype, out IntPtr actualtype, out int actualformat, out IntPtr nitems, out IntPtr bytesafter, out IntPtr data);
		[DllImport(LIBX11)] private static extern int XSetTransientForHint(IntPtr display, IntPtr window, IntPtr parent);
		[DllImport(LIBX11)] private static extern int XFree(IntPtr data);
		[DllImport(LIBX11)] private static extern int XFlush(IntPtr display);

		// IntPtr.Zero when there is no X display, for example a Wayland session without XWayland
		public static IntPtr Open()
		{
			try { return XOpenDisplay(IntPtr.Zero); }
			catch(Exception) { return IntPtr.Zero; }
		}

		// XCloseDisplay syncs, so a queued async X error surfaces here; with MONO_XEXCEPTIONS set it would throw out of Run
		public static void Close(IntPtr display)
		{
			try { XCloseDisplay(display); }
			catch(Exception) { }
		}

		// Never applies without an EWMH window manager, which has no stacking problem to fix either
		public static bool TryApply(IntPtr display, int pid, long parent)
		{
			IntPtr root = XDefaultRootWindow(display);
			long[] clients = GetLongs(display, root, XInternAtom(display, "_NET_CLIENT_LIST", false), XA_WINDOW);
			if(clients == null || clients.Length == 0) return false;

			IntPtr pidatom = XInternAtom(display, "_NET_WM_PID", false);
			IntPtr transientatom = XInternAtom(display, "WM_TRANSIENT_FOR", false);
			long[] pids = new long[clients.Length];
			bool[] hastransient = new bool[clients.Length];
			for(int i = 0; i < clients.Length; i++)
			{
				long[] value = GetLongs(display, new IntPtr(clients[i]), pidatom, XA_CARDINAL);
				pids[i] = (value != null && value.Length > 0 ? value[0] : 0);
				if(pids[i] != pid) continue;
				value = GetLongs(display, new IntPtr(clients[i]), transientatom, XA_WINDOW);
				hastransient[i] = (value != null && value.Length > 0 && value[0] != 0);
			}

			long chooser = ZenityArgs.PickChooser(clients, pids, hastransient, pid);
			if(chooser == 0) return false;
			XSetTransientForHint(display, new IntPtr(chooser), new IntPtr(parent));
			XFlush(display);
			return true;
		}

		// A format-32 property as longs (Xlib stores each item in a C long), or null when missing or the window is gone
		private static long[] GetLongs(IntPtr display, IntPtr window, IntPtr property, int type)
		{
			IntPtr actualtype, nitems, bytesafter, data;
			int format;
			if(XGetWindowProperty(display, window, property, IntPtr.Zero, new IntPtr(4096), false, new IntPtr(type), out actualtype, out format, out nitems, out bytesafter, out data) != 0)
				return null;
			try
			{
				if(data == IntPtr.Zero || format != 32) return null;
				long[] result = new long[nitems.ToInt64()];
				for(int i = 0; i < result.Length; i++) result[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64();
				return result;
			}
			finally
			{
				if(data != IntPtr.Zero) XFree(data);
			}
		}
	}
#endif
}
