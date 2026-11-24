#region ================== Namespaces

using System;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Rendering;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// 2D View tab: linedef thickness, the 2D vertex scale and the text label settings, plus the label colours on the Appearance tab
	internal partial class PreferencesForm
	{
		#region ================== 2D View tab

		private const string SAME_SIZE = "(same)";

		private TabPage tabview2d;
		private TransparentTrackBar linedefthickness;
		private Label linedefthicknesslabel;
		private ComboBox[] textlabelkindsizes;
		private TransparentTrackBar textlabelbackalpha;
		private Label textlabelbackalphalabel;
		private ColorControl colorsectorlabels;
		private ColorControl colormeasurelabels;

		private static readonly TextLabelKind[] SIZE_KINDS = { TextLabelKind.SectorLabel, TextLabelKind.SelectionNumber, TextLabelKind.Measurement, TextLabelKind.EventLabel };
		private static readonly string[] SIZE_KIND_NAMES = { "Sector labels:", "Selection numbers:", "Measurements:", "Event line labels:" };

		private void BuildView2DTab()
		{
			tabview2d = new TabPage("2D View");
			tabview2d.UseVisualStyleBackColor = true;

			// Lines
			GroupBox lines = new GroupBox { Text = " Lines ", Location = new Point(8, 8), Size = new Size(331, 70) };
			linedefthickness = MakeSlider(127, 20, 1, ProgramConfiguration.MAX_LINEDEF_THICKNESS, General.Settings.LinedefThickness);
			linedefthicknesslabel = new Label { AutoSize = true, Location = new Point(249, 32) };
			linedefthickness.ValueChanged += (s, e) => UpdateLinedefThicknessLabel();
			UpdateLinedefThicknessLabel();
			lines.Controls.AddRange(new Control[] { MakeLabel("Linedef thickness:", 26, 32, false), linedefthickness, linedefthicknesslabel });
			toolTip1.SetToolTip(linedefthickness, "Width of linedefs in 2D modes, in pixels");

			// Vertices: the existing vertex scale slider, moved from the Interface tab
			GroupBox vertices = new GroupBox { Text = " Vertices ", Location = new Point(8, 84), Size = new Size(331, 70) };
			Control interfacegroup = vertexScale.Parent;
			int freedtop = vertexScale.Top;
			MoveControl(label22, vertices, label22.Left, label22.Top - freedtop + 20);
			MoveControl(vertexScaleLabel, vertices, vertexScaleLabel.Left, vertexScaleLabel.Top - freedtop + 20);
			MoveControl(vertexScale, vertices, vertexScale.Left, 20);
			CloseGap(interfacegroup, freedtop);

			// Text labels: the existing font group, moved from the Interface tab, plus per-kind sizes and the background
			Control labelsparent = groupBox11.Parent;
			int labelstop = groupBox11.Top;
			groupBox11.Parent.Controls.Remove(groupBox11);
			groupBox11.Location = new Point(345, 8);
			if(labelsparent != null && groupBox3.Parent == labelsparent && groupBox3.Top > labelstop) groupBox3.Top = labelstop;

			textlabelkindsizes = new ComboBox[SIZE_KINDS.Length];
			int y = 56;
			for(int i = 0; i < SIZE_KINDS.Length; i++, y += 27)
			{
				ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, y - 3), Size = new Size(70, 21) };
				combo.Items.Add(SAME_SIZE);
				foreach(object item in textlabelfontsize.Items) combo.Items.Add(item);
				int size = General.Settings.GetTextLabelKindSize(SIZE_KINDS[i]);
				if(size <= 0) combo.SelectedIndex = 0;
				else
				{
					string text = size.ToString();
					int index = combo.Items.IndexOf(text);
					if(index < 0) index = combo.Items.Add(text);
					combo.SelectedIndex = index;
				}
				textlabelkindsizes[i] = combo;
				groupBox11.Controls.Add(MakeLabel(SIZE_KIND_NAMES[i], 16, y, false));
				groupBox11.Controls.Add(combo);
				toolTip1.SetToolTip(combo, SAME_SIZE + " uses the size above");
			}

			textlabelbackalpha = MakeSlider(127, y, 0, 100, General.Settings.TextLabelBackAlpha);
			textlabelbackalpha.LargeChange = 10;
			textlabelbackalpha.TickFrequency = 10;
			textlabelbackalphalabel = new Label { AutoSize = true, Location = new Point(249, y + 12) };
			textlabelbackalpha.ValueChanged += (s, e) => UpdateTextLabelBackAlphaLabel();
			UpdateTextLabelBackAlphaLabel();
			groupBox11.Controls.AddRange(new Control[] { MakeLabel("Background:", 16, y + 12, false), textlabelbackalpha, textlabelbackalphalabel });
			toolTip1.SetToolTip(textlabelbackalpha, "Opacity of the box behind sector labels, selection numbers and measurements");
			groupBox11.Size = new Size(groupBox11.Width, y + 55);

			tabview2d.Controls.AddRange(new Control[] { lines, vertices, groupBox11 });
			tabs.TabPages.Insert(tabs.TabPages.IndexOf(tabcolors) + 1, tabview2d);

			// Label colours sit with the other colours on the Appearance tab
			colorsectorlabels = MakeColor("Sector labels:", 365, General.Colors.SectorLabels);
			colormeasurelabels = MakeColor("Measurements:", 394, General.Colors.MeasureLabels);
			colorsgroup1.Controls.AddRange(new Control[] { colorsectorlabels, colormeasurelabels });

			// Every tab added past two rows of headers takes height from the pages, so grow the form instead
			Load += (s, e) => FitTabPages();
		}

		private void FitTabPages()
		{
			int needed = 0;
			foreach(TabPage page in tabs.TabPages)
				foreach(Control c in page.Controls)
					if(c.Dock == DockStyle.None && (c.Anchor & AnchorStyles.Bottom) == 0) needed = Math.Max(needed, c.Bottom + 8);
			int deficit = needed - tabs.DisplayRectangle.Height;
			if(deficit > 0)
			{
				Height += deficit;
				CenterToParent();
			}
		}

		private void ApplyView2DSettings()
		{
			General.Settings.LinedefThickness = linedefthickness.Value;
			for(int i = 0; i < SIZE_KINDS.Length; i++)
			{
				int size;
				if(!int.TryParse(textlabelkindsizes[i].Text, out size)) size = 0;
				General.Settings.SetTextLabelKindSize(SIZE_KINDS[i], size);
			}
			General.Settings.TextLabelBackAlpha = textlabelbackalpha.Value;
			General.Colors.SectorLabels = colorsectorlabels.Color;
			General.Colors.MeasureLabels = colormeasurelabels.Color;
		}

		private void UpdateLinedefThicknessLabel()
		{
			linedefthicknesslabel.Text = linedefthickness.Value + " px" + (linedefthickness.Value == 1 ? " (default)" : "");
		}

		private void UpdateTextLabelBackAlphaLabel()
		{
			textlabelbackalphalabel.Text = textlabelbackalpha.Value + "%" + (textlabelbackalpha.Value == 100 ? " (default)" : "");
		}

		private static TransparentTrackBar MakeSlider(int x, int y, int min, int max, int value)
		{
			TransparentTrackBar t = new TransparentTrackBar
			{
				BackColor = Color.Transparent,
				LargeChange = 1,
				Location = new Point(x, y),
				Size = new Size(116, 45),
				Minimum = min,
				Maximum = max,
				TickFrequency = 1,
				TickStyle = TickStyle.TopLeft
			};
			t.Value = General.Clamp(value, min, max);
			return t;
		}

		private static ColorControl MakeColor(string label, int y, PixelColor color)
		{
			ColorControl c = new ColorControl
			{
				BackColor = Color.Transparent,
				Label = label,
				Location = new Point(15, y),
				MaximumSize = new Size(10000, 23),
				MinimumSize = new Size(100, 23),
				Size = new Size(168, 23)
			};
			c.Color = color;
			return c;
		}

		private static void MoveControl(Control control, Control parent, int x, int y)
		{
			if(control.Parent != null) control.Parent.Controls.Remove(control);
			control.Location = new Point(x, y);
			parent.Controls.Add(control);
		}

		// Moves the rows below a removed row up by one row pitch and shrinks the group to match
		private static void CloseGap(Control group, int top)
		{
			if(group == null) return;
			int next = int.MaxValue;
			foreach(Control c in group.Controls)
				if(c.Top > top) next = Math.Min(next, c.Top);
			if(next == int.MaxValue) return;
			int shift = next - top;
			foreach(Control c in group.Controls)
				if(c.Top > top) c.Top -= shift;
			group.Height -= shift;
		}

		#endregion
	}
}
