#region ================== Namespaces

using System;
using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Drags toolbar strips by their grip. Neither Mono nor a plain Panel parent gives a native toolstrip
	// drag, so this runs the same on both platforms: moves only show an indicator, release commits.
	internal sealed class ToolbarDragController : IMessageFilter
	{
		#region ================== Variables

		private readonly Form form;
		private readonly ToolbarDock dock;
		private readonly Panel indicator;

		// .NET's ToolStrip raises neither MouseMove nor MouseUp over its own items, so a press is
		// followed by polling the pointer and buttons as well
		private readonly Timer poll;

		private ToolStrip pressed; // Strip pressed in its grip band, before or during a drag
		private string pressedid;
		private Point pressclient; // Press point in form client coordinates
		private int grab; // Press point along the strip from its start
		private bool dragging;
		private bool cancelled; // Escape ended the drag; the press lasts until the button is released
		private bool ending;
		private bool swallowescapeup;
		private ToolbarDropTarget target;

		#endregion

		#region ================== Constructor

		public ToolbarDragController(Form form, ToolbarDock dock)
		{
			this.form = form;
			this.dock = dock;

			indicator = new Panel();
			indicator.Name = "toolbardropindicator";
			indicator.BackColor = SystemColors.Highlight;
			indicator.Visible = false;
			form.Controls.Add(indicator);

			poll = new Timer();
			poll.Interval = 50;
			poll.Tick += poll_Tick;
			Application.AddMessageFilter(this);

			foreach(ToolStrip strip in dock.Strips)
			{
				strip.MouseDown += strip_MouseDown;
				strip.MouseMove += strip_MouseMove;
				strip.MouseCaptureChanged += strip_MouseCaptureChanged;
				strip.MouseLeave += strip_MouseLeave;
			}
		}

		#endregion

		#region ================== Mouse

		// The painted grip band before the items
		private static bool InGripBand(ToolStrip strip, Point p)
		{
			Rectangle d = strip.DisplayRectangle;
			return (strip.Orientation == Orientation.Horizontal) ? (p.X >= 0 && p.X < d.Left) : (p.Y >= 0 && p.Y < d.Top);
		}

		private void strip_MouseDown(object sender, MouseEventArgs e)
		{
			ToolStrip strip = (ToolStrip)sender;
			if(e.Button != MouseButtons.Left || dock.Locked || pressed != null || !InGripBand(strip, e.Location)) return;
			pressed = strip;
			pressedid = dock.IdOf(strip);
			if(pressedid == null) { pressed = null; return; }
			pressclient = form.PointToClient(strip.PointToScreen(e.Location));
			grab = (strip.Orientation == Orientation.Horizontal) ? e.X : e.Y;
			strip.Capture = true;
			poll.Start();
		}

		private void strip_MouseMove(object sender, MouseEventArgs e)
		{
			ToolStrip strip = (ToolStrip)sender;
			if(pressed == null || strip != pressed)
			{
				// Show where a drag can start
				Cursor c = (!dock.Locked && InGripBand(strip, e.Location)) ? Cursors.SizeAll : Cursors.Default;
				if(strip.Cursor != c) strip.Cursor = c;
				return;
			}

			Track(form.PointToClient(strip.PointToScreen(e.Location)));
		}

		private void poll_Tick(object sender, EventArgs e)
		{
			if(pressed == null) { poll.Stop(); return; }
			if((Control.MouseButtons & MouseButtons.Left) == 0) End(dragging);
			else Track(form.PointToClient(Control.MousePosition));
		}

		// p is in form client coordinates
		private void Track(Point p)
		{
			if(cancelled) return;
			if(!dragging)
			{
				Size threshold = SystemInformation.DragSize;
				if(Math.Abs(p.X - pressclient.X) < threshold.Width && Math.Abs(p.Y - pressclient.Y) < threshold.Height) return;
				dragging = true;
				pressed.Cursor = Cursors.SizeAll;
			}

			UpdateTarget(p);
		}

		// The release ends the press and reaches no control: Mono clicks a strip item on any mouse-up over it
		public bool PreFilterMessage(ref Message m)
		{
			const int WM_LBUTTONUP = 0x0202;
			if(pressed == null || m.Msg != WM_LBUTTONUP) return false;
			End(dragging);
			return true;
		}

		// Losing capture to something else cancels
		private void strip_MouseCaptureChanged(object sender, EventArgs e)
		{
			if(!ending && pressed != null && sender == pressed && !pressed.Capture) End(false);
		}

		private void strip_MouseLeave(object sender, EventArgs e)
		{
			ToolStrip strip = (ToolStrip)sender;
			if(strip != pressed && strip.Cursor != Cursors.Default) strip.Cursor = Cursors.Default;
		}

		#endregion

		#region ================== Keyboard

		// Called first from the form's key handlers; Escape cancels a drag and goes no further
		public bool HandleKeyDown(KeyEventArgs e)
		{
			if(!dragging || e.KeyCode != Keys.Escape) return false;
			dragging = false;
			cancelled = true;
			target = new ToolbarDropTarget();
			indicator.Visible = false;
			pressed.Cursor = Cursors.Default;
			swallowescapeup = true;
			e.Handled = true;
			e.SuppressKeyPress = true;
			return true;
		}

		public bool HandleKeyUp(KeyEventArgs e)
		{
			if(!swallowescapeup || e.KeyCode != Keys.Escape) return false;
			swallowescapeup = false;
			e.Handled = true;
			return true;
		}

		#endregion

		#region ================== Drop

		private void UpdateTarget(Point p)
		{
			target = dock.NormalizeTarget(pressedid, dock.Model.HitTest(pressedid, p, dock.GetGeometry(), grab));
			if(!target.Valid)
			{
				indicator.Visible = false;
				return;
			}

			Rectangle current = dock.BoundsOf(pressedid);
			int length = ToolbarLayoutModel.IsVertical(dock.EdgeOf(pressedid)) ? current.Height : current.Width;
			Rectangle r = dock.TargetBounds(pressedid, target, Math.Max(length, 8));
			if(!target.NewRow)
			{
				// Joining a row: an outline would need a hollow window, so mark the strip's start instead
				int bar = Math.Max(3, (int)(3 * MainForm.DPIScaler.Width));
				r = ToolbarLayoutModel.IsVertical(target.Edge) ? new Rectangle(r.X, r.Y, r.Width, bar) : new Rectangle(r.X, r.Y, bar, r.Height);
			}

			indicator.Bounds = r;
			if(!indicator.Visible) indicator.Visible = true;
			indicator.BringToFront();
		}

		private void End(bool commit)
		{
			ending = true;
			poll.Stop();
			ToolStrip strip = pressed;
			string id = pressedid;
			bool wasdragging = dragging;
			ToolbarDropTarget t = target;

			pressed = null;
			pressedid = null;
			dragging = false;
			cancelled = false;
			target = new ToolbarDropTarget();
			indicator.Visible = false;
			if(strip != null)
			{
				strip.Capture = false;
				strip.Cursor = Cursors.Default;
			}
			ending = false;

			if(commit && wasdragging && id != null && t.Valid) dock.Commit(id, t);
		}

		#endregion
	}
}
