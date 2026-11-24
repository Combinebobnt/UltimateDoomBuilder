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

		// Per burst: a mismatch layout can't fix (MinimumSize, MaximumSize) stops after this many
		private const int MAX_RELAYOUTS = 20;

		internal static int TotalRelayouts { get; private set; }

		private sealed class Guard
		{
			public bool Pending;
			public int Count;
			public Action<string> Log;
		}

		private static readonly ConditionalWeakTable<Control, Guard> guards = new ConditionalWeakTable<Control, Guard>();

		// A log callback replaces any earlier one
		public static void Attach(Control control, Action<string> log = null)
		{
			if(!Enabled || control == null) return;
			Guard guard;
			if(guards.TryGetValue(control, out guard))
			{
				if(log != null) guard.Log = log;
				return;
			}
			guard = new Guard { Log = log };
			guards.Add(control, guard);
			control.SizeChanged += OnSizeChanged;
			control.HandleDestroyed += delegate { guard.Pending = false; };
		}

		// Every control, since Dock is often set after the control is added; covers later additions too
		public static void AttachTree(Control root)
		{
			if(!Enabled || root == null) return;
			Attach(root);
			root.ControlAdded -= OnControlAdded;
			root.ControlAdded += OnControlAdded;
			foreach(Control child in root.Controls) AttachTree(child);
		}

		// Mono's dock layout gives a Fill child the parent's DisplayRectangle, less docked siblings
		internal static Size DockFillSize(Control control) { return control.Parent.DisplayRectangle.Size; }

		internal static bool NeedsRelayout(Control control)
		{
			if(control.Dock != DockStyle.Fill || control.Parent == null || control.IsDisposed) return false;
			foreach(Control sibling in control.Parent.Controls)
				if(sibling != control && sibling.Visible && sibling.Dock != DockStyle.None) return false;
			return control.Size != DockFillSize(control);
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
				TotalRelayouts++;
				Size fill = DockFillSize(control);
				if(guard.Log != null) guard.Log("relayout size=" + control.Width + "x" + control.Height + " parent=" + fill.Width + "x" + fill.Height);
				control.Parent.PerformLayout();
				control.Invalidate();
				if(!NeedsRelayout(control)) guard.Count = 0;
			});
		}
	}
}
