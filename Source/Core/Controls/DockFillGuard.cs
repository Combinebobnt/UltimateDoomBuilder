#region ================== Namespaces

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	// Mono/X11 can read a docked child's ConfigureNotify from an earlier layout pass after a later
	// resize and adopt that old size, which no parent layout follows up on, so a Dock=Fill control
	// sticks at it inside a larger parent. This lays the parent out again when that happens.
	public static class DockFillGuard
	{
		internal static readonly bool Enabled = Type.GetType("Mono.Runtime") != null;

		private const int MAX_RELAYOUTS = 20;

		private sealed class Guard
		{
			public bool Pending;
			public int Count;
			public Action<string> Log;
		}

		private static readonly ConditionalWeakTable<Control, Guard> guards = new ConditionalWeakTable<Control, Guard>();

		// Guards one control; a log callback replaces any earlier one
		public static void Attach(Control control, Action<string> log = null)
		{
			if(!Enabled || control == null) return;
			Guard guard;
			if(guards.TryGetValue(control, out guard))
			{
				if(log != null) guard.Log = log;
				return;
			}
			guards.Add(control, new Guard { Log = log });
			control.SizeChanged += OnSizeChanged;
		}

		// Guards every Dock=Fill control under root, now and for controls added later
		public static void AttachTree(Control root)
		{
			if(!Enabled || root == null) return;
			if(root.Dock == DockStyle.Fill) Attach(root);
			root.ControlAdded -= OnControlAdded;
			root.ControlAdded += OnControlAdded;
			foreach(Control child in root.Controls) AttachTree(child);
		}

		// Mono's DisplayRectangle already excludes the parent's Padding
		internal static Size DockFillSize(Control control) { return control.Parent.DisplayRectangle.Size; }

		// Only a sole child has a fill size that doesn't depend on its siblings' docking
		internal static bool NeedsRelayout(Control control)
		{
			return control.Dock == DockStyle.Fill && control.Parent != null && control.Parent.Controls.Count == 1
				   && !control.IsDisposed && control.Size != DockFillSize(control);
		}

		private static void OnControlAdded(object sender, ControlEventArgs e) { AttachTree(e.Control); }

		private static void OnSizeChanged(object sender, EventArgs e)
		{
			Control control = (Control)sender;
			Guard guard;
			if(!guards.TryGetValue(control, out guard)) return;
			if(guard.Pending || guard.Count >= MAX_RELAYOUTS || !control.IsHandleCreated || !NeedsRelayout(control)) return;

			guard.Pending = true;
			control.BeginInvoke((Action)delegate
			{
				guard.Pending = false;
				if(!NeedsRelayout(control)) return;
				guard.Count++;
				Size fill = DockFillSize(control);
				if(guard.Log != null) guard.Log("relayout size=" + control.Width + "x" + control.Height + " parent=" + fill.Width + "x" + fill.Height);
				control.Parent.PerformLayout();
				control.Invalidate();
			});
		}
	}
}
