#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	// Paints a control tree synchronously, invalidating only controls that are newly visible or moved since the last pass.
	// A changed property invalidates its own control; a re-shown or moved one waits for an X expose otherwise.
	internal sealed class ControlTreePainter
	{
		private Dictionary<Control, Rectangle> painted = new Dictionary<Control, Rectangle>();
		private Dictionary<Control, Rectangle> next = new Dictionary<Control, Rectangle>();
		private readonly HashSet<Control> watched = new HashSet<Control>();
		private readonly Action<Control> invalidate;
		private readonly Action<Control, Rectangle> invalidaterect;
		private readonly Action<Control> update;

		public ControlTreePainter() : this(c => c.Invalidate(), (c, r) => c.Invalidate(r), c => c.Update()) { }

		internal ControlTreePainter(Action<Control> invalidate, Action<Control, Rectangle> invalidaterect, Action<Control> update)
		{
			this.invalidate = invalidate;
			this.invalidaterect = invalidaterect;
			this.update = update;
		}

		public void Paint(Control root)
		{
			Walk(root);
			Dictionary<Control, Rectangle> swap = painted;
			painted = next;
			next = swap;
			next.Clear();
		}

		// Forget the previous pass so the next one repaints everything
		public void Reset()
		{
			painted.Clear();
		}

		private void Walk(Control c)
		{
			Rectangle bounds = c.Bounds;
			Rectangle old;
			if(!painted.TryGetValue(c, out old) || old != bounds) invalidate(c);

			// Mono doesn't invalidate the parent area a child moved or shrank off
			foreach(Control child in c.Controls)
			{
				Rectangle oldchild;
				if(child.Visible && painted.TryGetValue(child, out oldchild) && oldchild != child.Bounds)
					invalidaterect(c, oldchild);
			}

			update(c);
			next[c] = bounds;
			if(watched.Add(c)) c.VisibleChanged += OnVisibleChanged;
			foreach(Control child in c.Controls)
				if(child.Visible) Walk(child);
		}

		// A control hidden and shown again between passes must count as new, as Mono doesn't invalidate it on show
		private void OnVisibleChanged(object sender, EventArgs e)
		{
			Forget((Control)sender);
		}

		private void Forget(Control c)
		{
			painted.Remove(c);
			foreach(Control child in c.Controls) Forget(child);
		}
	}
}
