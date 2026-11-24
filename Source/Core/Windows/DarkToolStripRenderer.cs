#region ================== Namespaces

using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Mono's professional renderer hardcodes white and black in places a color table can't reach
	internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
	{
		public DarkToolStripRenderer() : base(new DarkColorTable())
		{
			RoundedEdges = false;
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			Color c;
			if(e.ToolStrip is ToolStripDropDown) c = ColorTable.ToolStripDropDownBackground;
			else if(e.ToolStrip is MenuStrip) c = ColorTable.MenuStripGradientBegin;
			else if(e.ToolStrip is StatusStrip) c = ColorTable.StatusStripGradientBegin;
			else c = ColorTable.ToolStripGradientBegin;

			using(SolidBrush b = new SolidBrush(c)) e.Graphics.FillRectangle(b, e.AffectedBounds);
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			ToolStripItem item = e.Item;
			if(!item.Enabled || (!item.Selected && !item.Pressed))
			{
				base.OnRenderMenuItemBackground(e);
				return;
			}

			Rectangle r = new Rectangle(Point.Empty, item.Size);
			if(item.IsOnDropDown) r = new Rectangle(2, 0, r.Width - 3, r.Height);
			else if(item.Pressed && item is ToolStripDropDownItem && ((ToolStripDropDownItem)item).HasDropDownItems) r.Height += 1;

			Color fill = (item.Pressed && !item.IsOnDropDown) ? ColorTable.MenuItemPressedGradientBegin : ColorTable.MenuItemSelected;
			using(SolidBrush b = new SolidBrush(fill)) e.Graphics.FillRectangle(b, r);
			using(Pen p = new Pen(item.Pressed && !item.IsOnDropDown ? ColorTable.MenuBorder : ColorTable.MenuItemBorder))
				e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
		}

		protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
		{
			ToolStripItem item = e.Item;
			Rectangle r = new Rectangle(Point.Empty, item.Size);
			Color fill = item.Pressed ? ColorTable.ButtonPressedGradientBegin : item.Selected ? ColorTable.ButtonSelectedGradientBegin : ColorTable.OverflowButtonGradientBegin;
			using(SolidBrush b = new SolidBrush(fill)) e.Graphics.FillRectangle(b, r);

			// A bar over a down arrow, centered near the bottom like Mono's own
			int cx = r.Width / 2;
			int y = r.Height - 9;
			Brush arrow = SystemBrushes.ControlText;
			e.Graphics.FillRectangle(arrow, cx - 2, y, 5, 1);
			e.Graphics.FillRectangle(arrow, cx - 2, y + 3, 5, 1);
			e.Graphics.FillRectangle(arrow, cx - 1, y + 4, 3, 1);
			e.Graphics.FillRectangle(arrow, cx, y + 5, 1, 1);
		}
	}
}
