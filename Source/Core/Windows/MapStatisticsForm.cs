#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Map.Statistics;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	public enum MapStatisticsTab
	{
		Overview,
		Tally,
		Things,
		Textures,
		Tags
	}

	// Map statistics in one modal window; each open recomputes, edits from a tab mark the others dirty
	public partial class MapStatisticsForm : DelayedForm
	{
		#region ================== Constants

		private const string FOOTER = "Health, armor and ammo are raw sums of pickup amounts: no caps, monster drops or skill ammo factors; weapons at their single player amount. Monster HP sums the spawn health of counted kills.";

		private enum TallyRow
		{
			Kills,
			Items,
			SecretThings,
			SecretSectors,
			SecretsTotal,
			MonsterHealth,
			Health,
			Armor,
			Ammo,
			Unresolved
		}

		#endregion

		#region ================== Variables

		private static MapStatisticsTab lasttab = MapStatisticsTab.Overview;

		private readonly bool[] dirty = { true, true, true, true, true };
		private bool tagsloaded;
		private bool loading;

		private List<OverviewRow> overview;
		private List<TextureUse> textures;
		private List<Thing> tallythings;
		private List<TallyColumn> tallycolumns;
		private List<Sector> secretsectors;
		private List<SkillInfo> skills;

		#endregion

		#region ================== Constructor

		// A null tab opens the one used last
		public MapStatisticsForm(MapStatisticsTab? tab)
		{
			InitializeComponent();
			General.ApplyDataGridViewFix(overviewgrid);
			General.ApplyDataGridViewFix(tallygrid);
			General.ApplyDataGridViewFix(texturesgrid);

			tallyfooter.Text = FOOTER;
			thingspanel.MapEdited += panel_MapEdited;
			tagspanel.MapEdited += panel_MapEdited;

			loading = true;
			modes.Items.AddRange(new object[] { "Single player", "Cooperative", "Deathmatch" });
			modes.SelectedIndex = 0;
			bool hexen = ThingSpawnFilter.UsesPlayerClasses(General.Map.Config.BaseGame);
			classes.Items.AddRange(new object[] { "Fighter", "Cleric", "Mage" });
			classes.SelectedIndex = 0;
			classlabel.Visible = hexen;
			classes.Visible = hexen;
			loading = false;

			lasttab = tab ?? lasttab;
			tabs.SelectedIndex = (int)lasttab;
			ShowTab(lasttab);
		}

		#endregion

		#region ================== Tabs

		private void ShowTab(MapStatisticsTab tab)
		{
			if(!dirty[(int)tab]) return;
			dirty[(int)tab] = false;

			Cursor.Current = Cursors.WaitCursor;
			switch(tab)
			{
				case MapStatisticsTab.Overview: SetupOverview(); break;
				case MapStatisticsTab.Tally: SetupTally(); break;
				case MapStatisticsTab.Things: thingspanel.Setup(); break;
				case MapStatisticsTab.Textures: SetupTextures(); break;
				case MapStatisticsTab.Tags: tagspanel.Setup(); tagsloaded = true; break;
			}
			Cursor.Current = Cursors.Default;
		}

		private void SetupOverview()
		{
			if(textures == null) textures = MapOverviewStats.Textures();
			overview = MapOverviewStats.Overview(textures);

			overviewgrid.Rows.Clear();
			Color warning = UITheme.Pick(Color.Red, UITheme.DarkWarningText);
			foreach(OverviewRow r in overview)
			{
				int i = overviewgrid.Rows.Add(r.Label, r.Value);
				if(r.OverLimit) overviewgrid.Rows[i].Cells[1].Style.ForeColor = warning;
			}
		}

		private void SetupTextures()
		{
			if(textures == null) textures = MapOverviewStats.Textures();

			texturesgrid.Rows.Clear();
			Color warning = UITheme.Pick(Color.Red, UITheme.DarkWarningText);
			foreach(TextureUse u in textures)
			{
				int i = texturesgrid.Rows.Add(u.Name, u.IsFlat ? "Flat" : "Texture", u.Uses, u.Missing ? "Missing" : string.Empty);
				texturesgrid.Rows[i].Tag = u;
				if(u.Missing) texturesgrid.Rows[i].Cells[3].Style.ForeColor = warning;
			}
		}

		private void SetupTally()
		{
			skills = ThingSpawnFilter.SortedSkills(General.Map.Config.Skills);
			secretsectors = MapOverviewStats.SecretSectors();

			ThingTallyResolver resolver = new ThingTallyResolver();
			tallythings = General.Map.Map.Things.ToList();
			List<TallyThing> records = new List<TallyThing>(tallythings.Count);
			for(int i = 0; i < tallythings.Count; i++)
			{
				Thing t = tallythings[i];
				IDictionary<string, bool> view = ThingSpawnFilter.UdmfView(General.Map.UDMF, t.GetFlags(), General.Map.Config.ThingFlagsTranslation);
				records.Add(new TallyThing(i, resolver.Resolve(t.Type), view));
			}

			TallyGameMode mode = (TallyGameMode)Math.Max(0, modes.SelectedIndex);
			int cls = classes.Visible ? classes.SelectedIndex + 1 : 0;
			tallycolumns = ThingTally.Compute(records, skills.Select(s => s.Index).ToList(), mode, cls);

			FillTallyGrid();
		}

		private void FillTallyGrid()
		{
			tallygrid.Rows.Clear();
			tallygrid.Columns.Clear();
			tallygrid.Columns.Add("row", string.Empty);
			tallygrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			foreach(SkillInfo s in skills)
			{
				int c = tallygrid.Columns.Add("skill" + s.Index, s.Title);
				tallygrid.Columns[c].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
				tallygrid.Columns[c].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			}
			foreach(DataGridViewColumn col in tallygrid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;

			AddTallyRow("Kills", TallyRow.Kills, null, c => c.Kills);
			AddTallyRow("Items", TallyRow.Items, null, c => c.Items);
			AddTallyRow("Secret things", TallyRow.SecretThings, null, c => c.SecretThings);
			AddTallyRow("Secret sectors", TallyRow.SecretSectors, null, c => secretsectors.Count);
			AddTallyRow("Secrets total", TallyRow.SecretsTotal, null, c => c.SecretThings + secretsectors.Count);
			AddTallyRow("Monster HP", TallyRow.MonsterHealth, null, c => c.MonsterHealth);
			AddTallyRow("Health", TallyRow.Health, null, c => c.Health);
			AddTallyRow("Armor", TallyRow.Armor, null, c => c.Armor);

			SortedSet<string> ammo = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach(TallyColumn c in tallycolumns) ammo.UnionWith(c.Ammo.Keys);
			foreach(string name in ammo)
			{
				string n = name;
				AddTallyRow("Ammo: " + n, TallyRow.Ammo, n, c => c.Ammo.TryGetValue(n, out long a) ? a : 0);
			}

			if(tallycolumns.Any(c => c.Unresolved > 0))
				AddTallyRow("Unresolved things", TallyRow.Unresolved, null, c => c.Unresolved);
		}

		private void AddTallyRow(string title, TallyRow kind, string ammo, Func<TallyColumn, long> value)
		{
			object[] cells = new object[tallycolumns.Count + 1];
			cells[0] = title;
			for(int i = 0; i < tallycolumns.Count; i++) cells[i + 1] = value(tallycolumns[i]);
			int r = tallygrid.Rows.Add(cells);
			tallygrid.Rows[r].Tag = kind;
		}

		#endregion

		#region ================== Selection

		internal static void SelectThings(List<Thing> things)
		{
			General.Map.Map.ClearAllSelected();
			if(things.Count == 0)
			{
				General.Interface.RedrawDisplay();
				return;
			}

			General.Editing.ChangeMode("ThingsMode");
			List<Vector2D> points = new List<Vector2D>();
			foreach(Thing t in things)
			{
				t.Selected = true;
				Vector2D p = t.Position;
				points.Add(p);
				points.Add(p + new Vector2D(t.Size * 2.0f, t.Size * 2.0f));
				points.Add(p + new Vector2D(t.Size * 2.0f, -t.Size * 2.0f));
				points.Add(p + new Vector2D(-t.Size * 2.0f, t.Size * 2.0f));
				points.Add(p + new Vector2D(-t.Size * 2.0f, -t.Size * 2.0f));
			}

			General.Map.Map.Update();
			FinishSelection(points);
		}

		internal static void SelectSectors(List<Sector> sectors)
		{
			General.Map.Map.ClearAllSelected();
			if(sectors.Count == 0)
			{
				General.Interface.RedrawDisplay();
				return;
			}

			General.Editing.ChangeMode("SectorsMode");
			ClassicMode mode = (ClassicMode)General.Editing.Mode;
			List<Vector2D> points = new List<Vector2D>();
			foreach(Sector s in sectors)
			{
				mode.SelectMapElement(s);
				foreach(Sidedef sd in s.Sidedefs)
				{
					points.Add(sd.Line.Start.Position);
					points.Add(sd.Line.End.Position);
				}
			}

			FinishSelection(points);
		}

		internal static void SelectLinedefs(ICollection<Linedef> linedefs)
		{
			General.Map.Map.ClearAllSelected();
			if(linedefs.Count == 0)
			{
				General.Interface.RedrawDisplay();
				return;
			}

			General.Editing.ChangeMode("LinedefsMode");
			List<Vector2D> points = new List<Vector2D>();
			foreach(Linedef l in linedefs)
			{
				l.Selected = true;
				points.Add(l.Start.Position);
				points.Add(l.End.Position);
			}

			General.Map.Map.Update();
			FinishSelection(points);
		}

		private static void FinishSelection(List<Vector2D> points)
		{
			General.Editing.Mode.UpdateSelectionInfo();
			RectangleF area = MapSet.CreateEmptyArea();
			foreach(Vector2D p in points) area = MapSet.IncreaseArea(area, p);

			// Make the area square, using the largest side
			if(area.Width > area.Height)
			{
				float delta = area.Width - area.Height;
				area.Y -= delta * 0.5f;
				area.Height += delta;
			}
			else
			{
				float delta = area.Height - area.Width;
				area.X -= delta * 0.5f;
				area.Width += delta;
			}

			area.Inflate(100f, 100f);
			ClassicMode mode = General.Editing.Mode as ClassicMode;
			if(mode != null) mode.CenterOnArea(area, 0.6f);
		}

		private List<Thing> ThingsAt(List<int> indices)
		{
			return indices.Select(i => tallythings[i]).Where(t => !t.IsDisposed).ToList();
		}

		#endregion

		#region ================== Clipboard

		private static string GridText(DataGridView grid, bool header)
		{
			StringBuilder sb = new StringBuilder();
			if(header)
				sb.Append(string.Join("\t", grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText))).Append(Environment.NewLine);
			foreach(DataGridViewRow row in grid.Rows)
				sb.Append(string.Join("\t", row.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value)))).Append(Environment.NewLine);
			return sb.ToString();
		}

		private static void SetClipboard(string text)
		{
			try { Clipboard.SetText(text); }
			catch(Exception e) { General.ShowWarningMessage("Could not copy to the clipboard: " + e.Message, MessageBoxButtons.OK); }
		}

		#endregion

		#region ================== Events

		private void panel_MapEdited(object sender, EventArgs e)
		{
			textures = null;
			for(int i = 0; i < dirty.Length; i++) dirty[i] = true;
			dirty[tabs.SelectedIndex] = false;
		}

		private void tabs_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(tabs.SelectedIndex < 0) return;
			lasttab = (MapStatisticsTab)tabs.SelectedIndex;
			ShowTab(lasttab);
		}

		private void modes_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(!loading && tallycolumns != null) SetupTally();
		}

		private void overviewcopy_Click(object sender, EventArgs e)
		{
			SetClipboard(MapOverviewStats.ToText(overview));
		}

		private void tallycopy_Click(object sender, EventArgs e)
		{
			SetClipboard(GridText(tallygrid, true));
		}

		private void tallygrid_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			if(e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 1 || tallycolumns == null) return;
			TallyColumn col = tallycolumns[e.ColumnIndex - 1];

			switch((TallyRow)tallygrid.Rows[e.RowIndex].Tag)
			{
				case TallyRow.Kills: SelectThings(ThingsAt(col.KillThings)); break;
				case TallyRow.Items: SelectThings(ThingsAt(col.ItemThings)); break;
				case TallyRow.SecretThings: SelectThings(ThingsAt(col.SecretThingList)); break;
				case TallyRow.Unresolved: SelectThings(ThingsAt(col.UnresolvedThings)); break;
				case TallyRow.SecretSectors: SelectSectors(secretsectors.Where(s => !s.IsDisposed).ToList()); break;
			}
		}

		private void texturesgrid_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			if(e.Button != MouseButtons.Left || e.RowIndex < 0) return;
			TextureUse u = texturesgrid.Rows[e.RowIndex].Tag as TextureUse;
			if(u == null) return;

			if(u.IsFlat) SelectSectors(u.Sectors.Where(s => !s.IsDisposed).ToList());
			else SelectLinedefs(u.Linedefs.Where(l => !l.IsDisposed).ToList());
		}

		private void apply_Click(object sender, EventArgs e)
		{
			// Labels are only written when the Tags tab was loaded, else OK would clear them
			if(tagsloaded) tagspanel.ApplyLabels();
			this.DialogResult = DialogResult.OK;
			this.Close();
		}

		private void cancel_Click(object sender, EventArgs e)
		{
			this.DialogResult = DialogResult.Cancel;
			this.Close();
		}

		#endregion
	}
}
