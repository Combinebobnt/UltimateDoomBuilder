
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using Action = CodeImp.DoomBuilder.Actions.Action;

#endregion

// This Form is a workaround for the slow drawing of the .NET Forms.
// By showing the Form at 0% Opacity it allows the .NET framework to complete
// drawing the Form first, then we set it to 100% Opacity to actually show it.
// To use this class properly, set the initial Opacity of your Form to 0.

namespace CodeImp.DoomBuilder.Windows
{
	public class DelayedForm : Form
	{
		// Variables
		protected readonly string configname; //mxd

		//mxd. Stored window size and location. Tracks location and size of FormWindowState.Normal window
		private Size windowsize = Size.Empty;
		private Point windowlocation = Point.Empty;

		#region ================== Window geometry debug logging

		// Diagnoses a rare Mono/X11 bug where the texture browser opens at the wrong frame size.
		// Logs to its own file because UDBuilder.log is replaced on every startup. Start reading
		// at the one-line-per-open `GEOMSUMMARY` records; the surrounding block has the detail.
		private static readonly string[] GEOMDEBUG_FORMS = { "texturebrowserform" };
		private const string GEOMDEBUG_FILE = "UDBuilder.geometry.log";
		private const long GEOMDEBUG_MAXBYTES = 5 * 1024 * 1024;

		// Off unless UDB_GEOMETRY_LOG is set to a non-empty value other than "0"
		internal static readonly bool GeomDebugLogging = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UDB_GEOMETRY_LOG"))
			&& Environment.GetEnvironmentVariable("UDB_GEOMETRY_LOG") != "0";

		private bool GeomDebugEnabled
		{
			get
			{
				if(!GeomDebugLogging || string.IsNullOrEmpty(configname)) return false;
				return Array.IndexOf(GEOMDEBUG_FORMS, configname) >= 0;
			}
		}

		// One id per form instance, so interleaved opens stay separable in the log
		private readonly int geomdebugid = System.Threading.Interlocked.Increment(ref geomdebugcounter);
		private static int geomdebugcounter;

		// Size we asked for in OnLoad, compared against what we actually got once the window
		// manager has answered. A bad open then tags itself in the log, so finding one is a
		// grep for GEOMDEBUG-MISMATCH rather than eyeballing hundreds of correct opens.
		private Size geomdebugrequested = Size.Empty;
		private bool geomdebugshown;

		// Inputs the summary line reports, so a bad open can be read without cross-referencing
		// the settings file (which the same open may already have rewritten by then).
		private string geomdebugsettings = "?";
		private string geomdebugnote = "";
		private bool geomdebuguserresized;
		private int geomdebuglateresizes;
		private bool geomdebugsummarised;

		// Free-form context a subclass wants attached to this open, e.g. which dialog was the
		// parent, or which branch a layout value came from. Appended to the summary line.
		internal void GeomDebugAddNote(string note)
		{
			if(!GeomDebugEnabled || string.IsNullOrEmpty(note)) return;
			geomdebugnote = (geomdebugnote.Length > 0 ? geomdebugnote + " " : "") + note;
		}

		// The log is never deleted on startup, so cap it. Months of ordinary use would otherwise
		// leave a file too large to be worth reading when the bug finally shows up.
		private static void GeomDebugRotate(string path)
		{
			try
			{
				System.IO.FileInfo fi = new System.IO.FileInfo(path);
				if(!fi.Exists || fi.Length < GEOMDEBUG_MAXBYTES) return;

				string previous = path + ".1";
				if(System.IO.File.Exists(previous)) System.IO.File.Delete(previous);
				System.IO.File.Move(path, previous);
			}
			catch(Exception)
			{
				// Never let instrumentation break the editor
			}
		}

		private void GeomDebug(string stage)
		{
			if(!GeomDebugEnabled) return;

			try
			{
				SizeF current;
				try { current = this.CurrentAutoScaleDimensions; }
				catch { current = SizeF.Empty; }

				string line = string.Format(
					"{0:yyyy-MM-dd HH:mm:ss.fff} [{1}#{2}] {3,-22} Size={4}x{5} Client={6}x{7} Bounds={8} Restore={9} State={10} Min={11}x{12} Max={13}x{14} AutoScale={15}/{16} Dpi={17} HandleCreated={18} Visible={19} Opacity={20:0.##} VScreen={21}",
					DateTime.Now, configname, geomdebugid, stage,
					this.Size.Width, this.Size.Height,
					this.ClientSize.Width, this.ClientSize.Height,
					this.Bounds, this.RestoreBounds, this.WindowState,
					this.MinimumSize.Width, this.MinimumSize.Height,
					this.MaximumSize.Width, this.MaximumSize.Height,
					this.AutoScaleDimensions, current, MainForm.DPIScaler,
					this.IsHandleCreated, this.Visible, this.Opacity,
					SystemInformation.VirtualScreen);

				string path = System.IO.Path.Combine(General.SettingsPath, GEOMDEBUG_FILE);
				GeomDebugRotate(path);
				System.IO.File.AppendAllText(path, line + Environment.NewLine);
			}
			catch(Exception)
			{
				// Never let instrumentation break the editor
			}
		}

		#endregion

		// Constructor
		protected DelayedForm()
		{
			//mxd. And now, let's hope this doesn't horribly break anything...
			if(!(this is MainForm))
			{
				this.KeyPreview = true;
				this.KeyUp += OnKeyUp;
			}

			//mxd. Only when running (this.DesignMode doesn't seem to cut it here,
			// probably because not this, but a child class is in design mode...)
			if(LicenseManager.UsageMode != LicenseUsageMode.Designtime)
			{
				configname = this.GetType().Name.ToLowerInvariant();
				General.Actions.BindMethods(this);
			}
		}

		//mxd
		protected override void OnLoad(EventArgs e)
		{
			// Let the base class know
			base.OnLoad(e);

			if(this.DesignMode) return;

			GeomDebug("OnLoad:entry");
			if(GeomDebugEnabled)
			{
				geomdebugsettings = "state=" + General.Settings.ReadSetting("windows." + configname + ".windowstate", -1)
								  + " w=" + General.Settings.ReadSetting("windows." + configname + ".sizewidth", -1)
								  + " h=" + General.Settings.ReadSetting("windows." + configname + ".sizeheight", -1)
								  + " x=" + General.Settings.ReadSetting("windows." + configname + ".positionx", int.MaxValue)
								  + " y=" + General.Settings.ReadSetting("windows." + configname + ".positiony", int.MaxValue)
								  + " border=" + this.FormBorderStyle + " maxbox=" + this.MaximizeBox;
				GeomDebug("OnLoad:settings " + geomdebugsettings);
			}

			// Restore location and size
			this.SuspendLayout();

			// Restore windowstate
			if(this.MaximizeBox)
			{
				this.WindowState = (FormWindowState)General.Settings.ReadSetting("windows." + configname + ".windowstate", (int)FormWindowState.Normal);
			}

			// Form size matters?
			if(this.FormBorderStyle == FormBorderStyle.Sizable || this.FormBorderStyle == FormBorderStyle.SizableToolWindow)
			{
				this.Size = new Size(General.Settings.ReadSetting("windows." + configname + ".sizewidth", this.Size.Width),
									 General.Settings.ReadSetting("windows." + configname + ".sizeheight", this.Size.Height));
			}

			geomdebugrequested = this.Size;
			GeomDebug("OnLoad:after-size");

			// Restore location
			Point validlocation = Point.Empty;
			Point location = new Point(General.Settings.ReadSetting("windows." + configname + ".positionx", int.MaxValue),
									   General.Settings.ReadSetting("windows." + configname + ".positiony", int.MaxValue));

			if(location.X < int.MaxValue && location.Y < int.MaxValue)
			{
				// Location withing screen bounds?
				Rectangle bounds = new Rectangle(location, this.Size);
				bounds.Inflate(16, 16); // Add some safety padding
				if(SystemInformation.VirtualScreen.IntersectsWith(bounds)) validlocation = location;
			}

			if(validlocation == Point.Empty && !(this is MainForm))
			{
				// Do the manual CenterParent...
				validlocation = new Point(General.MainWindow.Location.X + General.MainWindow.Width / 2 - this.Width / 2,
										  General.MainWindow.Location.Y + General.MainWindow.Height / 2 - this.Height / 2);
			}

			// Apply location
			if(validlocation == Point.Empty)
			{
				this.StartPosition = FormStartPosition.CenterParent;
			}
			else
			{
				this.StartPosition = FormStartPosition.Manual;
				this.Location = validlocation;
			}

			GeomDebug("OnLoad:after-location");

			// Show the form if needed
			if(this.Opacity < 1.0) this.Opacity = 1.0;

			GeomDebug("OnLoad:after-opacity");

			this.ResumeLayout();

			GeomDebug("OnLoad:after-resumelayout");
		}

		//mxd. Instrumentation only - see the geometry debug region above.
		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);

			GeomDebug("OnShown");
			geomdebugshown = true;

			// Sample again once the window manager has had a chance to answer our resize request.
			// This is what separates "we asked for the wrong size" from "we asked correctly and
			// the WM overrode us via a late ConfigureNotify". Two samples, because a WM that
			// answers late is the leading suspect and 250ms may simply be too early to see it.
			if(GeomDebugEnabled)
			{
				GeomDebugSample(250, false);
				GeomDebugSample(1500, true);
			}
		}

		// One delayed geometry sample. The last one also writes the per-open summary line.
		private void GeomDebugSample(int delay, bool summarise)
		{
			Timer t = new Timer { Interval = delay };
			t.Tick += delegate
			{
				t.Stop();
				t.Dispose();

				bool mismatch = GeomDebugMismatched;
				GeomDebug((mismatch ? "GEOMDEBUG-MISMATCH " : "") + "PostShown+" + delay + "ms requested="
						  + geomdebugrequested.Width + "x" + geomdebugrequested.Height);

				if(summarise) GeomDebugSummary();
			};
			t.Start();
		}

		// One line per open, whichever comes first: the last delayed sample, or the window
		// closing. A quick open-and-pick disposes the form before the 1500ms sample ticks, and
		// that is exactly the case a bad open is most likely to be - the user shuts a
		// wrong-sized window straight away rather than working in it.
		private void GeomDebugSummary()
		{
			if(!GeomDebugEnabled || geomdebugsummarised) return;
			geomdebugsummarised = true;

			GeomDebug("GEOMSUMMARY " + (GeomDebugMismatched ? "BAD  " : "ok   ")
					  + "requested=" + geomdebugrequested.Width + "x" + geomdebugrequested.Height
					  + " final=" + this.Size.Width + "x" + this.Size.Height
					  + " state=" + this.WindowState
					  + " settings(" + geomdebugsettings + ")"
					  + (geomdebugnote.Length > 0 ? " " + geomdebugnote : ""));
		}

		private bool GeomDebugMismatched
		{
			get
			{
				return !geomdebugrequested.IsEmpty && this.Size != geomdebugrequested
					   && this.WindowState == FormWindowState.Normal;
			}
		}

		//mxd. When form is closing
		protected override void OnClosing(CancelEventArgs e)
		{
			if(e.Cancel) return;

			// Let the base class know
			base.OnClosing(e);

			GeomDebugSummary();

			// Determine window state to save
			if(this.MaximizeBox)
			{
				int windowstate;
				if(this.WindowState != FormWindowState.Minimized)
					windowstate = (int)this.WindowState;
				else
					windowstate = (int)FormWindowState.Normal;

				General.Settings.WriteSetting("windows." + configname + ".windowstate", windowstate);
			}

			// Form size matters?
			if(this.FormBorderStyle == FormBorderStyle.Sizable || this.FormBorderStyle == FormBorderStyle.SizableToolWindow)
			{
				Size size = ((windowsize.IsEmpty && this.WindowState == FormWindowState.Normal) ? this.Size : windowsize); // Prefer stored size if it was set
				if(!size.IsEmpty)
				{
					General.Settings.WriteSetting("windows." + configname + ".sizewidth", size.Width);
					General.Settings.WriteSetting("windows." + configname + ".sizeheight", size.Height);
				}
			}

			// Save location
			Point location = ((windowlocation.IsEmpty && this.WindowState == FormWindowState.Normal) ? this.Location : windowlocation); // Prefer stored location if it was set
			if(!location.IsEmpty)
			{
				General.Settings.WriteSetting("windows." + configname + ".positionx", location.X);
				General.Settings.WriteSetting("windows." + configname + ".positiony", location.Y);
			}

			GeomDebug("OnClosing tracked=" + windowsize.Width + "x" + windowsize.Height
					  + " loc=" + windowlocation.X + "," + windowlocation.Y
					  + " -> saved " + location.X + "," + location.Y);
		}

		//mxd. Also triggered when the window is dragged.
		protected override void OnResizeEnd(EventArgs e)
		{
			// Store location and size when window is not minimized or maximized
			if(this.WindowState == FormWindowState.Normal)
			{
				// Form size matters?
				if(this.FormBorderStyle == FormBorderStyle.Sizable || this.FormBorderStyle == FormBorderStyle.SizableToolWindow)
					windowsize = this.Size;
				windowlocation = this.Location;
			}

			// A drag-resize ends here. Past this point every size differs from the one we asked
			// for by design, so stop treating a post-shown resize as evidence of anything.
			geomdebuguserresized = true;
			GeomDebug("OnResizeEnd tracked=" + windowsize.Width + "x" + windowsize.Height);

			base.OnResizeEnd(e);
		}

		//mxd. Instrumentation only - a spurious Resize during show would be the tell for a
		//window manager overriding the size we asked for in OnLoad. Before OnShown everything
		//is logged; afterwards only resizes that move us off the requested size, which keeps
		//drag-resize noise out while still catching a late override.
		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			if(!geomdebugshown)
			{
				GeomDebug("OnResize");
			}
			else if(!geomdebuguserresized && geomdebuglateresizes < 5 && GeomDebugMismatched)
			{
				geomdebuglateresizes++;
				GeomDebug("GEOMDEBUG-LATERESIZE OnResize:post-shown requested="
						  + geomdebugrequested.Width + "x" + geomdebugrequested.Height);
			}
		}

		//mxd. Special handling to call "save screenshot" actions from any form, 
		//which inherits form DelayedForm and either doesn't override OnKeyUp, or calls base.OnKeyUp
		private void OnKeyUp(object sender, KeyEventArgs e)
		{
			e.Handled = (Form.ActiveForm == this && ProcessSaveScreenshotAction((int)e.KeyData));
			if(e.Handled) e.SuppressKeyPress = true;
		}

		// mxd. Handle screenshot saving from any form
		private static bool ProcessSaveScreenshotAction(int key)
		{
			Action[] actions = General.Actions.GetActionsByKey(key);
			foreach(Action action in actions) 
			{
				if(action.ShortName == "savescreenshot" || action.ShortName == "saveeditareascreenshot")
				{
					General.Actions.InvokeAction(action.Name);
					return true;
				}
			}

			return false;
		}

		//mxd
		[EndAction("savescreenshot", BaseAction = true)]
		internal void SaveScreenshot() 
		{
			if(Form.ActiveForm == this) General.MainWindow.SaveScreenshot(false);
		}

		//mxd
		[EndAction("saveeditareascreenshot", BaseAction = true)]
		internal void SaveEditAreaScreenshot() 
		{
			if(Form.ActiveForm == this) General.MainWindow.SaveScreenshot(true);
		}
	}
}
