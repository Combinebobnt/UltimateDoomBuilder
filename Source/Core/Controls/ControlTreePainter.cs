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
		private readonly Action<Control> invalidate;
		private readonly Action<Control> update;

		public ControlTreePainter() : this(c => c.Invalidate(), c => c.Update()) { }

		internal ControlTreePainter(Action<Control> invalidate, Action<Control> update)
		{
			this.invalidate = invalidate;
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
			update(c);
			next[c] = bounds;
			foreach(Control child in c.Controls)
				if(child.Visible) Walk(child);
		}
	}
}
