#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	internal enum DarkControlKind
	{
		Other,
		// Not walked, children included: the map view and the script editor color themselves (Preferences)
		Skipped,
		Form,
		Button,
		CheckBox,
		RadioButton,
		Label,
		GroupBox,
		TextBox,
		RichTextBox,
		ListBox,
		TreeView,
		ListView,
		ScrollBar,
		ScrollableContainer,
		NumericUpDown,
		UpDownButtons,
		ComboBox,
		TabControl,
		TabPage,
		DataGridView,
		ToolStrip,
	}

	// What the rules need to know about a control
	internal struct DarkControlInfo
	{
		public DarkControlKind Kind;
		public Color BackColor;
		public Color ForeColor;
		public FlatStyle FlatStyle;
		public Appearance Appearance;
		public ComboBoxStyle DropDownStyle;
		public bool Multiline;
		public bool AutoScroll;
		public bool OwnerDraw;
	}

	internal sealed class DarkControlChanges
	{
		public Color? BackColor;
		public Color? ForeColor;
		public FlatStyle? FlatStyle;
		public bool NoVisualStyleBackColor;
		public Color? FlatBorder;
		// Applied on every handle creation
		public string WindowTheme;
		public bool Titlebar;
		public bool OwnerDrawTabs;
		public bool DarkGrid;
		// The change only reaches a handle created after it
		public bool NeedsNewHandle;
	}

	// Pure decisions for the Windows dark walker, testable without creating controls
	internal static class DarkControlRules
	{
		public const string EXPLORER_THEME = "DarkMode_Explorer";
		public const string COMBOBOX_THEME = "DarkMode_CFD";

		// A system color's dark palette value; null for named, transparent, ARGB and unpaletted system colors
		public static Color? Convert(Color c)
		{
			if(!c.IsSystemColor) return null;
			Color dark;
			return UITheme.DarkPalette.TryGetValue(c.ToKnownColor(), out dark) ? dark : (Color?)null;
		}

		public static DarkControlChanges Decide(DarkControlInfo info, bool dark)
		{
			DarkControlChanges ch = new DarkControlChanges();
			if(!dark || info.Kind == DarkControlKind.Skipped) return ch;

			ch.BackColor = Convert(info.BackColor);
			ch.ForeColor = Convert(info.ForeColor);
			if(info.OwnerDraw) return ch;

			switch(info.Kind)
			{
				case DarkControlKind.Form:
					ch.Titlebar = true;
					break;

				case DarkControlKind.Button:
					FlatButton(info, ch);
					break;

				case DarkControlKind.CheckBox:
				case DarkControlKind.RadioButton:
					if(info.Appearance == Appearance.Button) FlatButton(info, ch);
					else LeaveSystemStyle(info, ch);
					break;

				// FlatStyle.System draws through Windows, which ignores ForeColor
				case DarkControlKind.Label:
				case DarkControlKind.GroupBox:
					LeaveSystemStyle(info, ch);
					break;

				case DarkControlKind.TextBox:
					if(info.Multiline) ch.WindowTheme = EXPLORER_THEME;
					break;

				case DarkControlKind.RichTextBox:
				case DarkControlKind.ListBox:
				case DarkControlKind.TreeView:
				case DarkControlKind.ListView:
				case DarkControlKind.ScrollBar:
				case DarkControlKind.UpDownButtons:
					ch.WindowTheme = EXPLORER_THEME;
					break;

				case DarkControlKind.ScrollableContainer:
					if(info.AutoScroll) ch.WindowTheme = EXPLORER_THEME;
					break;

				case DarkControlKind.ComboBox:
					if(info.DropDownStyle == ComboBoxStyle.DropDownList) ch.WindowTheme = COMBOBOX_THEME;
					else if(info.FlatStyle != FlatStyle.Flat)
					{
						ch.FlatStyle = FlatStyle.Flat;
						ch.NeedsNewHandle = info.FlatStyle == FlatStyle.System;
					}
					break;

				case DarkControlKind.TabControl:
					ch.OwnerDrawTabs = true;
					ch.NeedsNewHandle = true;
					break;

				case DarkControlKind.TabPage:
					ch.NoVisualStyleBackColor = true;
					break;

				case DarkControlKind.DataGridView:
					ch.DarkGrid = true;
					break;
			}
			return ch;
		}

		private static void FlatButton(DarkControlInfo info, DarkControlChanges ch)
		{
			if(info.FlatStyle != FlatStyle.Standard && info.FlatStyle != FlatStyle.System) return;
			ch.FlatStyle = FlatStyle.Flat;
			ch.NoVisualStyleBackColor = true;
			ch.FlatBorder = UITheme.DarkPalette[KnownColor.ControlLight];
			ch.NeedsNewHandle = info.FlatStyle == FlatStyle.System;
		}

		private static void LeaveSystemStyle(DarkControlInfo info, DarkControlChanges ch)
		{
			if(info.FlatStyle != FlatStyle.System) return;
			ch.FlatStyle = FlatStyle.Standard;
			ch.NeedsNewHandle = true;
		}
	}

	// Gives a form's native controls concrete dark colors and themes, and keeps them dark as code changes them
	internal sealed class DarkControlWalker
	{
		private sealed class Walked { }

		private static readonly MethodInfo recreatehandle = typeof(Control).GetMethod("RecreateHandle", BindingFlags.NonPublic | BindingFlags.Instance);
		// A UserPaint tab control (the script editor's) paints its own tabs
		private static readonly MethodInfo getstyle = typeof(Control).GetMethod("GetStyle", BindingFlags.NonPublic | BindingFlags.Instance);

		private readonly INativeTheming native;
		private readonly int titlebarattribute;
		private readonly ConditionalWeakTable<Control, Walked> walked = new ConditionalWeakTable<Control, Walked>();
		private readonly SolidBrush tabback = new SolidBrush(UITheme.DarkPalette[KnownColor.Control]);
		private readonly SolidBrush tabselected = new SolidBrush(UITheme.DarkPalette[KnownColor.ControlLight]);
		private readonly Color tabtext = UITheme.DarkPalette[KnownColor.ControlText];

		public DarkControlWalker(INativeTheming native, int titlebarattribute)
		{
			this.native = native;
			this.titlebarattribute = titlebarattribute;
		}

		public bool IsWalked(Control c)
		{
			Walked w;
			return walked.TryGetValue(c, out w);
		}

		// creating: the window being created when this runs (the CBT hook's), which misses changes needing a new handle
		public void Theme(Control c, IntPtr creating)
		{
			if(c == null || IsWalked(c)) return;
			walked.Add(c, new Walked());

			DarkControlInfo info = Describe(c);
			DarkControlChanges ch = DarkControlRules.Decide(info, true);
			if(info.Kind == DarkControlKind.Skipped) return;

			if(ch.BackColor.HasValue) c.BackColor = ch.BackColor.Value;
			if(ch.ForeColor.HasValue) c.ForeColor = ch.ForeColor.Value;
			c.BackColorChanged += OnBackColorChanged;
			c.ForeColorChanged += OnForeColorChanged;

			if(ch.FlatStyle.HasValue) SetFlatStyle(c, ch.FlatStyle.Value);
			if(ch.NoVisualStyleBackColor)
			{
				if(c is ButtonBase) ((ButtonBase)c).UseVisualStyleBackColor = false;
				else if(c is TabPage) ((TabPage)c).UseVisualStyleBackColor = false;
				// A visual-style TabPage read Transparent; now it reads its TabControl's system color, with no change event
				OnBackColorChanged(c, EventArgs.Empty);
			}
			if(c is TextBoxBase) ((TextBoxBase)c).ReadOnlyChanged += OnReadOnlyChanged;
			if(ch.FlatBorder.HasValue && c is ButtonBase) ((ButtonBase)c).FlatAppearance.BorderColor = ch.FlatBorder.Value;
			if(ch.OwnerDrawTabs)
			{
				TabControl tabs = (TabControl)c;
				tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
				tabs.DrawItem += OnDrawTab;
			}
			if(ch.DarkGrid) DarkenGrid((DataGridView)c);

			if(ch.WindowTheme != null || ch.Titlebar)
			{
				if(c.IsHandleCreated) OnHandle(c, ch);
				c.HandleCreated += (s, e) => OnHandle(c, ch);
			}
			if(ch.NeedsNewHandle && !c.IsHandleCreated && creating != IntPtr.Zero)
			{
				EventHandler check = null;
				check = (s, e) =>
				{
					c.HandleCreated -= check;
					if(c.Handle == creating) c.BeginInvoke(new Action(() => recreatehandle.Invoke(c, null)));
				};
				c.HandleCreated += check;
			}

			c.ControlAdded += OnControlAdded;
			if(c is ToolStrip)
			{
				ToolStrip strip = (ToolStrip)c;
				foreach(ToolStripItem item in strip.Items) ThemeHosted(item);
				strip.ItemAdded += OnToolStripItemAdded;
			}

			foreach(Control child in c.Controls) Theme(child, creating);
		}

		private void OnHandle(Control c, DarkControlChanges ch)
		{
			if(ch.WindowTheme != null) native.SetWindowTheme(c.Handle, ch.WindowTheme);
			if(ch.Titlebar)
			{
				native.AllowDarkForWindow(c.Handle);
				native.SetTitlebarDark(c.Handle, titlebarattribute);
			}
		}

		private static DarkControlInfo Describe(Control c)
		{
			DarkControlInfo info = new DarkControlInfo { Kind = Classify(c), BackColor = c.BackColor, ForeColor = c.ForeColor };
			if(c is ButtonBase)
			{
				info.FlatStyle = ((ButtonBase)c).FlatStyle;
				if(c is CheckBox) info.Appearance = ((CheckBox)c).Appearance;
				else if(c is RadioButton) info.Appearance = ((RadioButton)c).Appearance;
			}
			else if(c is Label) info.FlatStyle = ((Label)c).FlatStyle;
			else if(c is GroupBox) info.FlatStyle = ((GroupBox)c).FlatStyle;
			else if(c is ComboBox)
			{
				ComboBox combo = (ComboBox)c;
				info.FlatStyle = combo.FlatStyle;
				info.DropDownStyle = combo.DropDownStyle;
				info.OwnerDraw = combo.DrawMode != DrawMode.Normal;
			}
			if(c is TextBoxBase) info.Multiline = ((TextBoxBase)c).Multiline;
			if(c is ScrollableControl) info.AutoScroll = ((ScrollableControl)c).AutoScroll;
			if(c is ListView) info.OwnerDraw = ((ListView)c).OwnerDraw;
			else if(c is ListBox) info.OwnerDraw = ((ListBox)c).DrawMode != DrawMode.Normal;
			else if(c is TreeView) info.OwnerDraw = ((TreeView)c).DrawMode != TreeViewDrawMode.Normal;
			else if(c is TabControl) info.OwnerDraw = ((TabControl)c).DrawMode != TabDrawMode.Normal || (bool)getstyle.Invoke(c, new object[] { ControlStyles.UserPaint });
			return info;
		}

		private static DarkControlKind Classify(Control c)
		{
			// The script editor is ScintillaNET's, which the Mono build doesn't reference
			if(c is RenderTargetControl || c.GetType().Namespace == "ScintillaNET") return DarkControlKind.Skipped;
			if(c is Form) return DarkControlKind.Form;
			if(c is CheckBox) return DarkControlKind.CheckBox;
			if(c is RadioButton) return DarkControlKind.RadioButton;
			if(c is ButtonBase) return DarkControlKind.Button;
			if(c is Label) return DarkControlKind.Label;
			if(c is GroupBox) return DarkControlKind.GroupBox;
			if(c is RichTextBox) return DarkControlKind.RichTextBox;
			if(c is TextBoxBase) return DarkControlKind.TextBox;
			if(c is ListBox) return DarkControlKind.ListBox;
			if(c is TreeView) return DarkControlKind.TreeView;
			if(c is ListView) return DarkControlKind.ListView;
			if(c is ScrollBar) return DarkControlKind.ScrollBar;
			if(c is NumericUpDown) return DarkControlKind.NumericUpDown;
			if(c.GetType().Name == "UpDownButtons") return DarkControlKind.UpDownButtons;
			if(c is ComboBox) return DarkControlKind.ComboBox;
			if(c is TabControl) return DarkControlKind.TabControl;
			if(c is TabPage) return DarkControlKind.TabPage;
			if(c is DataGridView) return DarkControlKind.DataGridView;
			if(c is ToolStrip) return DarkControlKind.ToolStrip;
			if(c is ScrollableControl) return DarkControlKind.ScrollableContainer;
			return DarkControlKind.Other;
		}

		private static void SetFlatStyle(Control c, FlatStyle style)
		{
			if(c is ButtonBase) ((ButtonBase)c).FlatStyle = style;
			else if(c is Label) ((Label)c).FlatStyle = style;
			else if(c is GroupBox) ((GroupBox)c).FlatStyle = style;
			else if(c is ComboBox) ((ComboBox)c).FlatStyle = style;
		}

		private static void DarkenGrid(DataGridView grid)
		{
			Color? c = DarkControlRules.Convert(grid.BackgroundColor);
			if(c.HasValue) grid.BackgroundColor = c.Value;
			c = DarkControlRules.Convert(grid.GridColor);
			if(c.HasValue) grid.GridColor = c.Value;
			grid.EnableHeadersVisualStyles = false;
			foreach(DataGridViewCellStyle style in new[] { grid.DefaultCellStyle, grid.ColumnHeadersDefaultCellStyle, grid.RowHeadersDefaultCellStyle })
			{
				c = DarkControlRules.Convert(style.BackColor);
				if(c.HasValue) style.BackColor = c.Value;
				c = DarkControlRules.Convert(style.ForeColor);
				if(c.HasValue) style.ForeColor = c.Value;
				c = DarkControlRules.Convert(style.SelectionBackColor);
				if(c.HasValue) style.SelectionBackColor = c.Value;
				c = DarkControlRules.Convert(style.SelectionForeColor);
				if(c.HasValue) style.SelectionForeColor = c.Value;
			}
		}

		// The converted value is no system color, so setting it doesn't come back here
		private static void OnBackColorChanged(object sender, EventArgs e)
		{
			Control c = (Control)sender;
			Color? dark = DarkControlRules.Convert(c.BackColor);
			if(dark.HasValue) c.BackColor = dark.Value;
		}

		private static void OnForeColorChanged(object sender, EventArgs e)
		{
			Control c = (Control)sender;
			Color? dark = DarkControlRules.Convert(c.ForeColor);
			if(dark.HasValue) c.ForeColor = dark.Value;
		}

		// A text box's default background follows ReadOnly (Control or Window), so the converted one must follow too
		private static void OnReadOnlyChanged(object sender, EventArgs e)
		{
			TextBoxBase box = (TextBoxBase)sender;
			int back = box.BackColor.ToArgb();
			if(back == UITheme.DarkPalette[KnownColor.Control].ToArgb() || back == UITheme.DarkPalette[KnownColor.Window].ToArgb())
				box.ResetBackColor();
		}

		private void OnControlAdded(object sender, ControlEventArgs e)
		{
			Theme(e.Control, IntPtr.Zero);
		}

		private void OnToolStripItemAdded(object sender, ToolStripItemEventArgs e)
		{
			ThemeHosted(e.Item);
		}

		private void ThemeHosted(ToolStripItem item)
		{
			ToolStripControlHost host = item as ToolStripControlHost;
			if(host != null) Theme(host.Control, IntPtr.Zero);
		}

		private void OnDrawTab(object sender, DrawItemEventArgs e)
		{
			TabControl tabs = (TabControl)sender;
			if(e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
			bool selected = e.Index == tabs.SelectedIndex;
			e.Graphics.FillRectangle(selected ? tabselected : tabback, e.Bounds);
			TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, e.Font, e.Bounds, tabtext,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
		}
	}
}
