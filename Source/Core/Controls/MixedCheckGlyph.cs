#region ================== Namespaces

using System;
using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	// Mono draws an indeterminate checkbox as a gray check, barely distinct from checked.
	// This overdraws it with the filled square Windows and GTK use for "mixed".
	public static class MixedCheckGlyph
	{
		internal static readonly bool Enabled = Type.GetType("Mono.Runtime") != null;

		// Mono's classic theme box for CheckAlign.MiddleLeft, with its 2px 3D border
		private const int BOX_SIZE = 13;
		private const int BORDER = 2;
		private const int MARK_INSET = 2;

		public static void Attach(CheckBox box)
		{
			if(!Enabled || box == null) return;
			box.Paint -= OnPaint;
			box.Paint += OnPaint;
		}

		public static void AttachTree(Control root)
		{
			if(!Enabled || root == null) return;
			CheckBox box = root as CheckBox;
			if(box != null) Attach(box);
			foreach(Control child in root.Controls) AttachTree(child);
		}

		internal static void Draw(Graphics g, int controlHeight, bool enabled)
		{
			int boxtop = (controlHeight - BOX_SIZE) / 2;
			Rectangle interior = new Rectangle(BORDER, boxtop + BORDER, BOX_SIZE - BORDER * 2, BOX_SIZE - BORDER * 2);
			Rectangle mark = Rectangle.Inflate(interior, -MARK_INSET, -MARK_INSET);

			using(SolidBrush back = new SolidBrush(enabled ? SystemColors.Window : SystemColors.Control))
				g.FillRectangle(back, interior);
			using(SolidBrush fore = new SolidBrush(enabled ? SystemColors.ControlText : SystemColors.GrayText))
				g.FillRectangle(fore, mark);
		}

		private static void OnPaint(object sender, PaintEventArgs e)
		{
			CheckBox box = sender as CheckBox;
			if(box == null || box.CheckState != CheckState.Indeterminate) return;

			// Any other layout puts the box elsewhere, so leave Mono's drawing alone
			if(box.CheckAlign != ContentAlignment.MiddleLeft || box.Appearance != Appearance.Normal
			   || box.FlatStyle != FlatStyle.Standard) return;

			Draw(e.Graphics, box.Height, box.Enabled);
		}
	}
}
