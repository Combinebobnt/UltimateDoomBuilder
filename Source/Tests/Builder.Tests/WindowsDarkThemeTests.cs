using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	// The rules run anywhere; the UI tests need real Win32 and pass vacuously elsewhere (Mono creates no control without X)
	[Collection("WindowsUI")]
	public class WindowsDarkThemeTests
	{
		private static Color Dark(KnownColor kc) { return UITheme.DarkPalette[kc]; }

		private static DarkControlInfo Info(DarkControlKind kind, FlatStyle flat = FlatStyle.Standard)
		{
			return new DarkControlInfo { Kind = kind, BackColor = SystemColors.Control, ForeColor = SystemColors.ControlText, FlatStyle = flat };
		}

		[Fact]
		public void ConvertReplacesOnlyPalettedSystemColors()
		{
			Assert.Equal(Dark(KnownColor.Window).ToArgb(), DarkControlRules.Convert(SystemColors.Window)!.Value.ToArgb());
			Assert.False(DarkControlRules.Convert(SystemColors.Window)!.Value.IsSystemColor);
			Assert.Null(DarkControlRules.Convert(Color.Red));
			Assert.Null(DarkControlRules.Convert(Color.Transparent));
			Assert.Null(DarkControlRules.Convert(Color.FromArgb(1, 2, 3)));
			// A system color the palette leaves alone
			Assert.Null(DarkControlRules.Convert(SystemColors.Desktop));
			// Already converted: no second round
			Assert.Null(DarkControlRules.Convert(Dark(KnownColor.Control)));
		}

		[Fact]
		public void GridBackgroundDefaultIsDarkOnBothRuntimes()
		{
			Assert.Equal(Dark(KnownColor.AppWorkspace).ToArgb(), DarkControlRules.ConvertGridBackground(SystemColors.AppWorkspace)!.Value.ToArgb());
			Assert.Equal(Dark(KnownColor.AppWorkspace).ToArgb(), DarkControlRules.ConvertGridBackground(SystemColors.ControlDark)!.Value.ToArgb());
			Assert.Equal(Dark(KnownColor.Window).ToArgb(), DarkControlRules.ConvertGridBackground(SystemColors.Window)!.Value.ToArgb());
			Assert.Null(DarkControlRules.ConvertGridBackground(Color.FromArgb(0x82, 0x82, 0x82)));
		}

		[Fact]
		public void LightMakesNoChanges()
		{
			foreach(DarkControlKind kind in Enum.GetValues(typeof(DarkControlKind)))
			{
				DarkControlChanges ch = DarkControlRules.Decide(Info(kind, FlatStyle.System), false);
				Assert.True(ch.BackColor == null && ch.ForeColor == null && ch.FlatStyle == null && ch.WindowTheme == null
					&& !ch.Titlebar && !ch.OwnerDrawTabs && !ch.DarkGrid && !ch.NoVisualStyleBackColor, kind.ToString());
			}
		}

		[Fact]
		public void RulesPerControlKind()
		{
			const string EXPLORER = DarkControlRules.EXPLORER_THEME;
			var cases = new (string name, DarkControlInfo info, Func<DarkControlChanges, bool> expected)[]
			{
				("form", Info(DarkControlKind.Form), ch => ch.Titlebar),
				("button", Info(DarkControlKind.Button), ch => ch.FlatStyle == FlatStyle.Flat && ch.NoVisualStyleBackColor && ch.FlatBorder.HasValue && !ch.NeedsNewHandle),
				("system button", Info(DarkControlKind.Button, FlatStyle.System), ch => ch.FlatStyle == FlatStyle.Flat && ch.NeedsNewHandle),
				("popup button", Info(DarkControlKind.Button, FlatStyle.Popup), ch => ch.FlatStyle == null),
				("checkbox", Info(DarkControlKind.CheckBox), ch => ch.FlatStyle == null),
				("button-like checkbox", new DarkControlInfo { Kind = DarkControlKind.CheckBox, Appearance = Appearance.Button, FlatStyle = FlatStyle.Standard }, ch => ch.FlatStyle == FlatStyle.Flat),
				("system radio", Info(DarkControlKind.RadioButton, FlatStyle.System), ch => ch.FlatStyle == FlatStyle.Standard && ch.NeedsNewHandle),
				("system label", Info(DarkControlKind.Label, FlatStyle.System), ch => ch.FlatStyle == FlatStyle.Standard),
				("system group", Info(DarkControlKind.GroupBox, FlatStyle.System), ch => ch.FlatStyle == FlatStyle.Standard),
				("label", Info(DarkControlKind.Label), ch => ch.FlatStyle == null),
				("textbox", Info(DarkControlKind.TextBox), ch => ch.WindowTheme == null),
				("multiline", new DarkControlInfo { Kind = DarkControlKind.TextBox, Multiline = true }, ch => ch.WindowTheme == EXPLORER),
				("richtext", Info(DarkControlKind.RichTextBox), ch => ch.WindowTheme == EXPLORER),
				("listbox", Info(DarkControlKind.ListBox), ch => ch.WindowTheme == EXPLORER),
				("tree", Info(DarkControlKind.TreeView), ch => ch.WindowTheme == EXPLORER),
				("list", Info(DarkControlKind.ListView), ch => ch.WindowTheme == EXPLORER),
				("scrollbar", Info(DarkControlKind.ScrollBar), ch => ch.WindowTheme == EXPLORER),
				("updown", Info(DarkControlKind.UpDownButtons), ch => ch.WindowTheme == EXPLORER),
				("panel", Info(DarkControlKind.ScrollableContainer), ch => ch.WindowTheme == null),
				("scrolling panel", new DarkControlInfo { Kind = DarkControlKind.ScrollableContainer, AutoScroll = true }, ch => ch.WindowTheme == EXPLORER),
				("droplist", new DarkControlInfo { Kind = DarkControlKind.ComboBox, DropDownStyle = ComboBoxStyle.DropDownList }, ch => ch.WindowTheme == DarkControlRules.COMBOBOX_THEME && ch.FlatStyle == null),
				("dropdown", new DarkControlInfo { Kind = DarkControlKind.ComboBox, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Standard }, ch => ch.FlatStyle == FlatStyle.Flat),
				("tabs", Info(DarkControlKind.TabControl), ch => ch.OwnerDrawTabs && ch.NeedsNewHandle),
				("owner-drawn tabs", new DarkControlInfo { Kind = DarkControlKind.TabControl, OwnerDraw = true, BackColor = SystemColors.Control }, ch => !ch.OwnerDrawTabs && ch.BackColor.HasValue),
				("owner-drawn list", new DarkControlInfo { Kind = DarkControlKind.ListView, OwnerDraw = true }, ch => ch.WindowTheme == null),
				("tab page", Info(DarkControlKind.TabPage), ch => ch.NoVisualStyleBackColor),
				("grid", Info(DarkControlKind.DataGridView), ch => ch.DarkGrid),
				("skipped", Info(DarkControlKind.Skipped), ch => ch.BackColor == null && ch.ForeColor == null),
				("named colors", new DarkControlInfo { Kind = DarkControlKind.Other, BackColor = Color.Red, ForeColor = Color.Transparent }, ch => ch.BackColor == null && ch.ForeColor == null),
				("system colors", Info(DarkControlKind.Other), ch => ch.BackColor!.Value.ToArgb() == Dark(KnownColor.Control).ToArgb() && ch.ForeColor!.Value.ToArgb() == Dark(KnownColor.ControlText).ToArgb()),
			};

			var failures = new List<string>();
			foreach(var (name, info, expected) in cases)
				if(!expected(DarkControlRules.Decide(info, true))) failures.Add(name);
			Assert.True(failures.Count == 0, string.Join(", ", failures));
		}

		#region ================== Real Win32 (CI)

		private sealed class RecordingNative : INativeTheming
		{
			public readonly List<(IntPtr hwnd, string call)> Calls = new List<(IntPtr, string)>();
			public void SetWindowTheme(IntPtr hwnd, string subappname) { Calls.Add((hwnd, "theme " + subappname)); }
			public void SetTitlebarDark(IntPtr hwnd, int attribute) { Calls.Add((hwnd, "titlebar " + attribute)); }
			public void AllowDarkForWindow(IntPtr hwnd) { Calls.Add((hwnd, "allowdark")); }
		}

		private const int GWL_STYLE = -16;
		private const int TCS_OWNERDRAWFIXED = 0x2000;

		[DllImport("user32.dll")]
		private static extern int GetWindowLong(IntPtr hwnd, int index);

		private static bool OnWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

		// Visual styles on, on a fresh STA thread
		private static void RunUI(Action action)
		{
			Exception error = null;
			var thread = new Thread(() =>
			{
				try
				{
					Application.EnableVisualStyles();
					Assert.True(Application.RenderWithVisualStyles, "visual styles are off");
					action();
				}
				catch(Exception e) { error = e; }
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join();
			if(error != null) ExceptionDispatchInfo.Capture(error).Throw();
		}

		private static void AssertDark(Color actual, KnownColor expected, string what)
		{
			Assert.True(!actual.IsSystemColor && actual.ToArgb() == Dark(expected).ToArgb(), what + " is " + actual);
		}

		[Fact]
		public void WalkerDarkensEveryKindAndKeepsLaterColorsDark()
		{
			if(!OnWindows) return;
			RunUI(() =>
			{
				var native = new RecordingNative();
				var walker = new DarkControlWalker(native, 20);
				using(var form = new Form())
				{
					var button = new Button { Text = "b" };
					var text = new TextBox { Multiline = true };
					var single = new TextBox();
					var list = new ListView { View = View.Details };
					var tree = new TreeView();
					var tabs = new TabControl();
					// As the designer makes them
					var page = new TabPage("one") { UseVisualStyleBackColor = true };
					tabs.TabPages.Add(page);
					tabs.TabPages.Add(new TabPage("two") { UseVisualStyleBackColor = true });
					var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
					var grid = new DataGridView();
					var named = new Label { BackColor = Color.Red };
					var hosted = new TextBox();
					var strip = new ToolStrip();
					strip.Items.Add(new ToolStripControlHost(hosted));
					var numeric = new NumericUpDown();
					var scroll = new VScrollBar();
					var rich = new RichTextBox();
					var listbox = new ListBox();
					var toggle = new CheckBox { Appearance = Appearance.Button, Text = "t" };
					var systemlabel = new Label { FlatStyle = FlatStyle.System, Text = "s" };
					var panel = new Panel { AutoScroll = true };
					var readonlybox = new TextBox { ReadOnly = true };
					form.Controls.AddRange(new Control[] { button, text, single, list, tree, tabs, combo, grid, named, strip,
						numeric, scroll, rich, listbox, toggle, systemlabel, panel, readonlybox });

					walker.Theme(form, IntPtr.Zero);
					form.Show();
					Application.DoEvents();

					AssertDark(form.BackColor, KnownColor.Control, "form");
					AssertDark(button.BackColor, KnownColor.Control, "button");
					Assert.Equal(FlatStyle.Flat, button.FlatStyle);
					AssertDark(text.BackColor, KnownColor.Window, "multiline");
					AssertDark(single.ForeColor, KnownColor.WindowText, "textbox text");
					AssertDark(list.BackColor, KnownColor.Window, "list");
					AssertDark(hosted.BackColor, KnownColor.Window, "hosted textbox");
					AssertDark(grid.BackgroundColor, KnownColor.AppWorkspace, "grid");
					Assert.False(grid.EnableHeadersVisualStyles);
					Assert.Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode);
					Assert.False(page.UseVisualStyleBackColor);
					AssertDark(page.BackColor, KnownColor.Control, "tab page");
					Assert.Equal(Color.Red, named.BackColor);
					Assert.Equal(FlatStyle.Flat, toggle.FlatStyle);
					Assert.Equal(FlatStyle.Standard, systemlabel.FlatStyle);
					AssertDark(systemlabel.ForeColor, KnownColor.ControlText, "system label");
					AssertDark(readonlybox.BackColor, KnownColor.Control, "read-only textbox");

					string explorer = "theme " + DarkControlRules.EXPLORER_THEME;
					Assert.Contains((form.Handle, "titlebar 20"), native.Calls);
					Assert.Contains((form.Handle, "allowdark"), native.Calls);
					Assert.Contains((text.Handle, explorer), native.Calls);
					Assert.Contains((list.Handle, explorer), native.Calls);
					Assert.Contains((tree.Handle, explorer), native.Calls);
					Assert.Contains((combo.Handle, "theme " + DarkControlRules.COMBOBOX_THEME), native.Calls);
					Assert.DoesNotContain((single.Handle, explorer), native.Calls);
					Assert.Contains((scroll.Handle, explorer), native.Calls);
					Assert.Contains((rich.Handle, explorer), native.Calls);
					Assert.Contains((listbox.Handle, explorer), native.Calls);
					Assert.Contains((panel.Handle, explorer), native.Calls);
					Control spinner = numeric.Controls.Cast<Control>().Single(c => c.GetType().Name == "UpDownButtons");
					Assert.Contains((spinner.Handle, explorer), native.Calls);

					// Code that sets a system color later, a read-only box made editable, and a control added later
					single.BackColor = SystemColors.Window;
					AssertDark(single.BackColor, KnownColor.Window, "reassigned textbox");
					readonlybox.ReadOnly = false;
					AssertDark(readonlybox.BackColor, KnownColor.Window, "editable textbox");
					var late = new Button { Text = "late" };
					form.Controls.Add(late);
					AssertDark(late.BackColor, KnownColor.Control, "late button");
					Assert.Equal(FlatStyle.Flat, late.FlatStyle);
				}
			});
		}

		// The hook themes a form while its first child is being created; only that child may need a second handle
		private static void HookThemesForm(bool tabsfirst)
		{
			var native = new RecordingNative();
			var backend = new WindowsThemeBackend(22000, native);
			backend.InstallFormTheming();
			try
			{
				using(var form = new Form())
				{
					var button = new Button { Text = "b" };
					var tabs = new TabControl();
					tabs.TabPages.Add("one");
					var created = new Dictionary<Control, int>();
					foreach(Control c in new Control[] { form, button, tabs, tabs.TabPages[0] })
					{
						created[c] = 0;
						Control counted = c;
						c.HandleCreated += (s, e) => created[counted]++;
					}
					// Whether the first tab handle missed owner drawing, i.e. the walk ran while it was being created
					bool? firstownerdrawn = null;
					tabs.HandleCreated += (s, e) => { if(firstownerdrawn == null) firstownerdrawn = (GetWindowLong(tabs.Handle, GWL_STYLE) & TCS_OWNERDRAWFIXED) != 0; };
					form.Controls.AddRange(tabsfirst ? new Control[] { tabs, button } : new Control[] { button, tabs });

					form.Show();
					Application.DoEvents();
					Application.DoEvents();

					Assert.True(backend.Walker.IsWalked(form), "form not themed");
					Assert.Equal(FlatStyle.Flat, button.FlatStyle);
					Assert.Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode);
					Assert.True((GetWindowLong(tabs.Handle, GWL_STYLE) & TCS_OWNERDRAWFIXED) != 0, "tab control created without owner drawing");
					Assert.Contains((form.Handle, "titlebar 20"), native.Calls);
					Assert.Equal(1, created[form]);
					Assert.Equal(1, created[button]);
					Assert.NotNull(firstownerdrawn);
					// Only a tab control created without owner drawing gets its second handle
					Assert.Equal(firstownerdrawn == true ? 1 : 2, created[tabs]);
					if(!tabsfirst) Assert.True(firstownerdrawn == true, "the walk ran late for a tab control created second");
				}
			}
			finally { backend.UninstallFormTheming(); }
		}

		[Fact]
		public void HookThemesFormsWithoutRecreatingHandles()
		{
			if(!OnWindows) return;
			RunUI(() => HookThemesForm(false));
		}

		[Fact]
		public void HookFixesTheChildBeingCreatedWhenItThemesTheForm()
		{
			if(!OnWindows) return;
			RunUI(() => HookThemesForm(true));
		}

		#endregion
	}
}
