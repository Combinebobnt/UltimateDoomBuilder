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
					&& !ch.Titlebar && !ch.OwnerDrawTabs && !ch.DarkGrid && !ch.NoVisualStyleBackColor && !ch.Subclass && !ch.PinForeColor, kind.ToString());
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
				("system group", Info(DarkControlKind.GroupBox, FlatStyle.System), ch => ch.FlatStyle == FlatStyle.Standard && ch.PinForeColor),
				("group", Info(DarkControlKind.GroupBox), ch => ch.FlatStyle == null && ch.PinForeColor),
				("label", Info(DarkControlKind.Label), ch => ch.FlatStyle == null),
				("textbox", Info(DarkControlKind.TextBox), ch => ch.WindowTheme == null),
				("multiline", new DarkControlInfo { Kind = DarkControlKind.TextBox, Multiline = true }, ch => ch.WindowTheme == EXPLORER),
				("richtext", Info(DarkControlKind.RichTextBox), ch => ch.WindowTheme == EXPLORER),
				("listbox", Info(DarkControlKind.ListBox), ch => ch.WindowTheme == EXPLORER),
				("tree", Info(DarkControlKind.TreeView), ch => ch.WindowTheme == EXPLORER),
				("list", Info(DarkControlKind.ListView), ch => ch.WindowTheme == EXPLORER && ch.Subclass),
				("scrollbar", Info(DarkControlKind.ScrollBar), ch => ch.WindowTheme == EXPLORER),
				("updown", Info(DarkControlKind.UpDownButtons), ch => ch.WindowTheme == EXPLORER),
				("panel", Info(DarkControlKind.ScrollableContainer), ch => ch.WindowTheme == null),
				("scrolling panel", new DarkControlInfo { Kind = DarkControlKind.ScrollableContainer, AutoScroll = true }, ch => ch.WindowTheme == EXPLORER),
				("droplist", new DarkControlInfo { Kind = DarkControlKind.ComboBox, DropDownStyle = ComboBoxStyle.DropDownList }, ch => ch.WindowTheme == DarkControlRules.COMBOBOX_THEME && ch.FlatStyle == null),
				("dropdown", new DarkControlInfo { Kind = DarkControlKind.ComboBox, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Standard }, ch => ch.FlatStyle == FlatStyle.Flat),
				("tabs", Info(DarkControlKind.TabControl), ch => ch.OwnerDrawTabs && ch.Subclass && ch.NeedsNewHandle),
				("owner-drawn tabs", new DarkControlInfo { Kind = DarkControlKind.TabControl, OwnerDraw = true, BackColor = SystemColors.Control }, ch => !ch.OwnerDrawTabs && !ch.Subclass && ch.BackColor.HasValue),
				("owner-drawn list", new DarkControlInfo { Kind = DarkControlKind.ListView, OwnerDraw = true }, ch => ch.WindowTheme == null && !ch.Subclass),
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
			public void SubclassForDark(Control c) { Calls.Add((c.Handle, "subclass")); }
			public bool SetAppMode(DarkAppModeCall call, bool dark) { Calls.Add((IntPtr.Zero, "appmode " + call + " " + dark)); return true; }
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
					Assert.Contains((tabs.Handle, "subclass"), native.Calls);
					Assert.Contains((list.Handle, "subclass"), native.Calls);
					Assert.DoesNotContain((tree.Handle, "subclass"), native.Calls);

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
			Assert.Equal(new[] { "appmode SetPreferredAppMode True", "appmode SetPreferredAppMode False" },
				native.Calls.Where(c => c.call.StartsWith("appmode")).Select(c => c.call).ToArray());
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

		#region ================== Render census (CI)

		private const int PW_CLIENTONLY = 1;
		private const int PW_RENDERFULLCONTENT = 2;
		private const int LVM_GETHEADER = 0x101F;
		// Text is the light part of a dark control
		private const double MAX_LIGHT_SHARE = 0.15;
		private const double MAX_SENTINEL_SHARE = 0.01;
		private static readonly Color SENTINEL = Color.Magenta;

		[DllImport("user32.dll")]
		private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, int flags);

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT { public int left, top, right, bottom; }

		// One of each control the walker handles, laid out apart so each has its own rectangle
		private sealed class CensusForm : IDisposable
		{
			public readonly Form Form = new Form { Text = "census", StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ClientSize = new Size(820, 520) };
			public readonly List<(string name, Control control)> Cells = new List<(string, Control)>();
			public readonly TabControl Tabs = new TabControl { Size = new Size(200, 100) };
			public readonly ListView List = new ListView { View = View.Details, Size = new Size(200, 100), FullRowSelect = true };
			public readonly GroupBox Group = new GroupBox { Text = "Group", Size = new Size(140, 70) };

			public CensusForm()
			{
				var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };
				Form.Controls.Add(flow);
				void Add(string name, Control c) { flow.Controls.Add(c); Cells.Add((name, c)); }

				Add("button", new Button { Text = "b", Size = new Size(120, 40) });
				Add("multiline", new TextBox { Multiline = true, Size = new Size(120, 60) });
				Add("textbox", new TextBox { Width = 120 });
				Add("read-only textbox", new TextBox { ReadOnly = true, Width = 120 });
				var tree = new TreeView { Size = new Size(120, 80) };
				tree.Nodes.Add("n");
				Add("tree", tree);
				var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
				combo.Items.Add("c");
				combo.SelectedIndex = 0;
				Add("droplist", combo);
				// Rows and a filling column, so the unthemed grid is mostly white cells
				var grid = new DataGridView { Size = new Size(160, 80), RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
				grid.Columns.Add("c", "c");
				grid.Rows.Add("x");
				Add("grid", grid);
				Add("numeric", new NumericUpDown { Width = 120 });
				Add("scrollbar", new VScrollBar { Size = new Size(17, 80) });
				Add("richtext", new RichTextBox { Size = new Size(120, 60) });
				var listbox = new ListBox { Size = new Size(120, 60) };
				listbox.Items.Add("l");
				Add("listbox", listbox);
				Add("toggle", new CheckBox { Appearance = Appearance.Button, Text = "t", Size = new Size(120, 30) });
				Add("system label", new Label { FlatStyle = FlatStyle.System, Text = "s", AutoSize = false, Size = new Size(120, 30) });
				// Both scrollbars showing
				Add("scrolling panel", new Panel { AutoScroll = true, AutoScrollMinSize = new Size(400, 400), Size = new Size(120, 80) });
				Tabs.TabPages.Add(new TabPage("one") { UseVisualStyleBackColor = true });
				Tabs.TabPages.Add(new TabPage("two") { UseVisualStyleBackColor = true });
				Add("tabs", Tabs);
				List.Columns.Add("Alpha", 90);
				List.Columns.Add("Beta", 90);
				List.Items.Add(new ListViewItem(new[] { "x", "y" }));
				Add("list", List);
				Add("group", Group);
				// The strip itself takes the tool strip renderer's colors, which this test doesn't install
				var hosted = new TextBox();
				var strip = new ToolStrip { Dock = DockStyle.None };
				strip.Items.Add(new ToolStripControlHost(hosted));
				flow.Controls.Add(strip);
				Cells.Add(("hosted textbox", hosted));
			}

			public void Dispose() { Form.Dispose(); }
		}

		private static Rectangle InForm(Form form, Control c)
		{
			return form.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
		}

		private static Rectangle WindowInForm(Form form, IntPtr hwnd)
		{
			RECT r;
			GetWindowRect(hwnd, out r);
			return form.RectangleToClient(Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom));
		}

		private sealed class Pixels
		{
			private readonly byte[] data;
			private readonly int stride;
			public readonly Rectangle Bounds;

			public Pixels(Bitmap bmp)
			{
				Bounds = new Rectangle(Point.Empty, bmp.Size);
				var locked = bmp.LockBits(Bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
				stride = locked.Stride;
				data = new byte[stride * bmp.Height];
				Marshal.Copy(locked.Scan0, data, 0, data.Length);
				bmp.UnlockBits(locked);
			}

			// Shares of light and sentinel pixels, and the count above a luminance
			public (double light, double sentinel, int above) Measure(Rectangle r, int threshold)
			{
				r.Intersect(Bounds);
				int total = r.Width * r.Height, light = 0, sentinel = 0, above = 0;
				for(int y = r.Top; y < r.Bottom; y++)
					for(int x = r.Left; x < r.Right; x++)
					{
						int i = y * stride + x * 3;
						int b = data[i], g = data[i + 1], red = data[i + 2];
						if(red == SENTINEL.R && g == SENTINEL.G && b == SENTINEL.B) sentinel++;
						double lum = 0.299 * red + 0.587 * g + 0.114 * b;
						if(lum > 200) light++;
						if(lum > threshold) above++;
					}
				return total == 0 ? (0, 1, 0) : ((double)light / total, (double)sentinel / total, above);
			}
		}

		private static void Pump()
		{
			for(int i = 0; i < 10; i++)
			{
				Application.DoEvents();
				Thread.Sleep(30);
			}
		}

		private static Bitmap CaptureClient(Form form)
		{
			var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
			using(Graphics g = Graphics.FromImage(bmp))
			{
				g.Clear(SENTINEL);
				IntPtr hdc = g.GetHdc();
				try { Assert.True(PrintWindow(form.Handle, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT), "PrintWindow failed"); }
				finally { g.ReleaseHdc(hdc); }
			}
			return bmp;
		}

		// Renders the census form, themed or not, and adds each failing rectangle to failures
		private static void Census(bool themed, string dir, List<string> failures, List<string> report)
		{
			int build = WindowsDarkSupport.CurrentBuild();
			WindowsThemeBackend? backend = themed ? new WindowsThemeBackend(build, new Win32ThemeNative()) : null;
			backend?.InstallFormTheming();
			try
			{
				using(var census = new CensusForm())
				{
					Form form = census.Form;
					form.Show();
					Pump();
					form.Refresh();
					Pump();
					string mode = themed ? "dark" : "light";
					if(themed && !backend!.Walker.IsWalked(form)) failures.Add("dark: the hook never themed the form");

					using(Bitmap bmp = CaptureClient(form))
					{
						bmp.Save(Path.Combine(dir, mode + ".png"), System.Drawing.Imaging.ImageFormat.Png);
						var pixels = new Pixels(bmp);
						foreach(var (name, control) in census.Cells)
						{
							var (light, sentinel, _) = pixels.Measure(InForm(form, control), 255);
							report.Add(string.Format("{0} {1}: light {2:P0}, sentinel {3:P0}", mode, name, light, sentinel));
							if(sentinel > MAX_SENTINEL_SHARE) failures.Add(string.Format("{0} {1}: {2:P0} never painted", mode, name, sentinel));
							else if(themed && light >= MAX_LIGHT_SHARE) failures.Add(string.Format("dark {0}: {1:P0} light", name, light));
							// The negative control: a census that can't see light controls proves nothing
							else if(!themed && light < MAX_LIGHT_SHARE) failures.Add(string.Format("light {0}: only {1:P0} light, the census is blind", name, light));
						}

						if(themed)
						{
							// Light text must show on the dark parts Windows draws itself
							Rectangle header = WindowInForm(form, SendMessage(census.List.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero));
							Rectangle tab = form.RectangleToClient(census.Tabs.RectangleToScreen(census.Tabs.GetTabRect(0)));
							Rectangle caption = InForm(form, census.Group);
							caption.Height = census.Group.Font.Height + 2;
							foreach(var (name, r) in new[] { ("list header text", header), ("tab text", tab), ("group caption", caption) })
							{
								int above = pixels.Measure(r, 128).above;
								report.Add(string.Format("dark {0}: {1} pixels above 128", name, above));
								if(above < 3) failures.Add("dark " + name + " is unreadable");
							}
						}
					}
				}
			}
			finally { backend?.UninstallFormTheming(); }
		}

		[Fact]
		public void RenderCensusFindsNoLightControls()
		{
			if(!OnWindows || !WindowsDarkSupport.For(WindowsDarkSupport.CurrentBuild()).Supported) return;
			string dir = Environment.GetEnvironmentVariable("UDB_THEME_CENSUS_DIR") ?? Path.Combine(AppContext.BaseDirectory, "theme-census");
			Directory.CreateDirectory(dir);
			var failures = new List<string>();
			var report = new List<string>();
			RunUI(() =>
			{
				// Unthemed first, before anything dark is installed in the process
				Census(false, dir, failures, report);
				Census(true, dir, failures, report);
			});
			File.WriteAllLines(Path.Combine(dir, "census.txt"), report);
			Assert.True(failures.Count == 0, string.Join("; ", failures));
		}

		#endregion
	}
}
