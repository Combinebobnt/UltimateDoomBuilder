#region ================== Namespaces

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	// Moves a control's border into its parent's client area: the control loses its border and
	// shrinks to its old client rectangle, and the parent paints the ring around it.
	// Used on Mono, where a control's non-client border paints late or not at all while Visual mode runs.
	// A converted control must keep the parent it had when converted.
	internal static class ParentDrawnBorder
	{
		#region ================== Types

		private sealed class Converted
		{
			public BorderStyle Style;
			public int Width;
			public Rectangle LastRing;
		}

		private sealed class Subscribed { }

		#endregion

		#region ================== Variables

		private static readonly ConditionalWeakTable<Control, Converted> converted = new ConditionalWeakTable<Control, Converted>();
		private static readonly ConditionalWeakTable<Control, Subscribed> parents = new ConditionalWeakTable<Control, Subscribed>();

		#endregion

		#region ================== Methods

		public static void Apply(params Control[] controls)
		{
			foreach(Control c in controls)
			{
				if(c == null || c.Parent == null || c.Dock != DockStyle.None) continue;

				Converted dummy;
				if(converted.TryGetValue(c, out dummy)) continue;

				BorderStyle style = GetBorderStyle(c);
				if(style == BorderStyle.None) continue;

				int width = (c.Width - c.ClientSize.Width) / 2;
				if(width != 1 && width != 2) continue;

				Rectangle outer = c.Bounds;
				Converted info = new Converted { Style = style, Width = width, LastRing = outer };
				converted.Add(c, info);

				SetBorderStyle(c, BorderStyle.None);
				c.Bounds = Rectangle.Inflate(outer, -width, -width);

				c.VisibleChanged += OnChildGeometryChanged;
				c.LocationChanged += OnChildGeometryChanged;
				c.SizeChanged += OnChildGeometryChanged;

				Subscribed sub;
				if(!parents.TryGetValue(c.Parent, out sub))
				{
					parents.Add(c.Parent, new Subscribed());
					c.Parent.Paint += OnParentPaint;
				}
			}
		}

		public static Rectangle OuterBounds(Control c)
		{
			Converted info;
			if(converted.TryGetValue(c, out info)) return Rectangle.Inflate(c.Bounds, info.Width, info.Width);
			return c.Bounds;
		}

		private static BorderStyle GetBorderStyle(Control c)
		{
			if(c is PictureBox) return ((PictureBox)c).BorderStyle;
			if(c is Panel) return ((Panel)c).BorderStyle;
			return BorderStyle.None;
		}

		private static void SetBorderStyle(Control c, BorderStyle style)
		{
			if(c is PictureBox) ((PictureBox)c).BorderStyle = style;
			else if(c is Panel) ((Panel)c).BorderStyle = style;
		}

		private static void DrawRing(Graphics g, Rectangle ring, BorderStyle style)
		{
			// The same colors Mono's own non-client border uses, which the dark theme remaps
			if(style == BorderStyle.Fixed3D)
				ControlPaint.DrawBorder3D(g, ring, Border3DStyle.Sunken);
			else
				ControlPaint.DrawBorder(g, ring, SystemColors.WindowFrame, ButtonBorderStyle.Solid);
		}

		#endregion

		#region ================== Events

		private static void OnParentPaint(object sender, PaintEventArgs e)
		{
			foreach(Control c in ((Control)sender).Controls)
			{
				Converted info;
				if(!c.Visible || !converted.TryGetValue(c, out info)) continue;
				Rectangle ring = Rectangle.Inflate(c.Bounds, info.Width, info.Width);
				if(e.ClipRectangle.IntersectsWith(ring)) DrawRing(e.Graphics, ring, info.Style);
			}
		}

		private static void OnChildGeometryChanged(object sender, EventArgs e)
		{
			Control c = (Control)sender;
			Converted info;
			if(c.Parent == null || !converted.TryGetValue(c, out info)) return;
			Rectangle ring = Rectangle.Inflate(c.Bounds, info.Width, info.Width);
			c.Parent.Invalidate(info.LastRing);
			c.Parent.Invalidate(ring);
			info.LastRing = ring;
		}

		#endregion
	}
}
