#region ================== Namespaces

using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Flat colors for menus, toolbars and status bars in the dark interface theme
	internal sealed class DarkColorTable : ProfessionalColorTable
	{
		internal static readonly Color Bar = UITheme.DarkPalette[KnownColor.Control];
		internal static readonly Color DropDown = Color.FromArgb(0x1B, 0x1B, 0x1C);
		internal static readonly Color Border = Color.FromArgb(0x3F, 0x3F, 0x46);
		internal static readonly Color Selected = Color.FromArgb(0x3E, 0x3E, 0x40);
		internal static readonly Color Pressed = UITheme.DarkPalette[KnownColor.Highlight];
		internal static readonly Color Checked = Color.FromArgb(0x1F, 0x3D, 0x5C);
		internal static readonly Color CheckedBorder = Color.FromArgb(0x3F, 0x6E, 0x9A);

		public DarkColorTable() { UseSystemColors = false; }

		public override Color ButtonCheckedGradientBegin { get { return Checked; } }
		public override Color ButtonCheckedGradientMiddle { get { return Checked; } }
		public override Color ButtonCheckedGradientEnd { get { return Checked; } }
		public override Color ButtonCheckedHighlight { get { return Checked; } }
		public override Color ButtonCheckedHighlightBorder { get { return CheckedBorder; } }
		public override Color ButtonPressedBorder { get { return CheckedBorder; } }
		public override Color ButtonPressedGradientBegin { get { return Pressed; } }
		public override Color ButtonPressedGradientMiddle { get { return Pressed; } }
		public override Color ButtonPressedGradientEnd { get { return Pressed; } }
		public override Color ButtonPressedHighlight { get { return Pressed; } }
		public override Color ButtonPressedHighlightBorder { get { return CheckedBorder; } }
		public override Color ButtonSelectedBorder { get { return Border; } }
		public override Color ButtonSelectedGradientBegin { get { return Selected; } }
		public override Color ButtonSelectedGradientMiddle { get { return Selected; } }
		public override Color ButtonSelectedGradientEnd { get { return Selected; } }
		public override Color ButtonSelectedHighlight { get { return Selected; } }
		public override Color ButtonSelectedHighlightBorder { get { return Border; } }
		public override Color CheckBackground { get { return Checked; } }
		public override Color CheckPressedBackground { get { return Pressed; } }
		public override Color CheckSelectedBackground { get { return Pressed; } }
		public override Color GripDark { get { return DropDown; } }
		public override Color GripLight { get { return Border; } }
		public override Color ImageMarginGradientBegin { get { return DropDown; } }
		public override Color ImageMarginGradientMiddle { get { return DropDown; } }
		public override Color ImageMarginGradientEnd { get { return DropDown; } }
		public override Color ImageMarginRevealedGradientBegin { get { return DropDown; } }
		public override Color ImageMarginRevealedGradientMiddle { get { return DropDown; } }
		public override Color ImageMarginRevealedGradientEnd { get { return DropDown; } }
		public override Color MenuBorder { get { return Border; } }
		public override Color MenuItemBorder { get { return Selected; } }
		public override Color MenuItemPressedGradientBegin { get { return DropDown; } }
		public override Color MenuItemPressedGradientMiddle { get { return DropDown; } }
		public override Color MenuItemPressedGradientEnd { get { return DropDown; } }
		public override Color MenuItemSelected { get { return Selected; } }
		public override Color MenuItemSelectedGradientBegin { get { return Selected; } }
		public override Color MenuItemSelectedGradientEnd { get { return Selected; } }
		public override Color MenuStripGradientBegin { get { return Bar; } }
		public override Color MenuStripGradientEnd { get { return Bar; } }
		public override Color OverflowButtonGradientBegin { get { return Bar; } }
		public override Color OverflowButtonGradientMiddle { get { return Bar; } }
		public override Color OverflowButtonGradientEnd { get { return Bar; } }
		public override Color RaftingContainerGradientBegin { get { return Bar; } }
		public override Color RaftingContainerGradientEnd { get { return Bar; } }
		public override Color SeparatorDark { get { return Border; } }
		public override Color SeparatorLight { get { return Border; } }
		public override Color StatusStripGradientBegin { get { return Bar; } }
		public override Color StatusStripGradientEnd { get { return Bar; } }
		public override Color ToolStripBorder { get { return Bar; } }
		public override Color ToolStripContentPanelGradientBegin { get { return Bar; } }
		public override Color ToolStripContentPanelGradientEnd { get { return Bar; } }
		public override Color ToolStripDropDownBackground { get { return DropDown; } }
		public override Color ToolStripGradientBegin { get { return Bar; } }
		public override Color ToolStripGradientMiddle { get { return Bar; } }
		public override Color ToolStripGradientEnd { get { return Bar; } }
		public override Color ToolStripPanelGradientBegin { get { return Bar; } }
		public override Color ToolStripPanelGradientEnd { get { return Bar; } }
	}
}
