#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	// Used tags and their labels: the Map Statistics window's Tags tab
	public partial class TagStatsPanel : UserControl
	{
		#region ================== Variables

		public event EventHandler MapEdited;

		#endregion

		#region ================== Constructor

		public TagStatsPanel()
		{
			InitializeComponent();
			General.ApplyDataGridViewFix(dataGridView);
		}

		#endregion

		#region ================== Methods

		// Keeps labels typed since the window opened across a rebuild after an edit
		public void Setup()
		{
			Dictionary<int, string> labels = new Dictionary<int, string>(General.Map.Options.TagLabels);
			foreach(DataGridViewRow row in dataGridView.Rows)
				labels[(int)row.Cells[0].Value] = (string)row.Cells[1].Value ?? string.Empty;

			HashSet<int> tags = new HashSet<int>();
			Dictionary<int, int> sectorsCountByTag = new Dictionary<int, int>();
			Dictionary<int, int> linedefsCountByTag = new Dictionary<int, int>();
			Dictionary<int, int> thingsCountByTag = new Dictionary<int, int>();

			foreach(Sector s in General.Map.Map.Sectors)
			{
				if(s.Tag == 0) continue;
				tags.UnionWith(s.Tags);
				foreach(int tag in s.Tags)
				{
					sectorsCountByTag.TryGetValue(tag, out int c);
					sectorsCountByTag[tag] = c + 1;
				}
			}

			if(General.Map.FormatInterface.HasLinedefTag)
			{
				foreach(Linedef l in General.Map.Map.Linedefs)
				{
					if(l.Tag == 0) continue;
					tags.UnionWith(l.Tags);
					foreach(int tag in l.Tags)
					{
						linedefsCountByTag.TryGetValue(tag, out int c);
						linedefsCountByTag[tag] = c + 1;
					}
				}
			}
			else
			{
				Linedefs.Visible = false;
			}

			if(General.Map.FormatInterface.HasThingTag)
			{
				foreach(Thing t in General.Map.Map.Things)
				{
					if(t.Tag == 0) continue;
					tags.Add(t.Tag);
					thingsCountByTag.TryGetValue(t.Tag, out int c);
					thingsCountByTag[t.Tag] = c + 1;
				}
			}
			else
			{
				Things.Visible = false;
			}

			dataGridView.Rows.Clear();
			foreach(int tag in tags)
			{
				DataGridViewRow row = new DataGridViewRow();
				row.Cells.Add(new DataGridViewTextBoxCell { Value = tag });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = labels.TryGetValue(tag, out string label) ? label : string.Empty });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = sectorsCountByTag.TryGetValue(tag, out int sc) ? sc : 0 });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = linedefsCountByTag.TryGetValue(tag, out int lc) ? lc : 0 });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = thingsCountByTag.TryGetValue(tag, out int tc) ? tc : 0 });
				dataGridView.Rows.Add(row);
			}

			dataGridView.Sort(TagColumn, ListSortDirection.Ascending);
		}

		// Writes the edited labels into the map options (the window's OK)
		public void ApplyLabels()
		{
			dataGridView.EndEdit();
			General.Map.Options.TagLabels.Clear();

			foreach(DataGridViewRow row in dataGridView.Rows)
			{
				string label = (string)row.Cells[1].Value;
				if(!string.IsNullOrEmpty(label))
					General.Map.Options.TagLabels[(int)row.Cells[0].Value] = label;
			}
		}

		private static List<T> WithTag<T>(IEnumerable<T> elements, Func<T, bool> hastag)
		{
			List<T> list = new List<T>();
			foreach(T e in elements)
				if(hastag(e)) list.Add(e);
			return list;
		}

		#endregion

		#region ================== Events

		private void dataGridView_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			if(e.ColumnIndex < 2 || e.RowIndex == -1) return;
			int tag = (int)dataGridView.Rows[e.RowIndex].Cells[0].Value;

			List<Sector> sectors = null;
			List<Linedef> linedefs = null;
			List<Thing> things = null;
			if(e.ColumnIndex == 2) sectors = WithTag(General.Map.Map.Sectors, s => s.Tags.Contains(tag));
			else if(e.ColumnIndex == 3) linedefs = WithTag(General.Map.Map.Linedefs, l => l.Tags.Contains(tag));
			else if(e.ColumnIndex == 4) things = WithTag(General.Map.Map.Things, t => t.Tag == tag);

			if(e.Button == MouseButtons.Left)
			{
				if(sectors != null) MapStatisticsForm.SelectSectors(sectors);
				else if(linedefs != null) MapStatisticsForm.SelectLinedefs(linedefs);
				else if(things != null) MapStatisticsForm.SelectThings(things);
			}
			else if(e.Button == MouseButtons.Right)
			{
				dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = true;

				if(sectors != null && sectors.Count > 0) General.MainWindow.ShowEditSectors(sectors);
				else if(linedefs != null && linedefs.Count > 0) General.MainWindow.ShowEditLinedefs(linedefs);
				else if(things != null && things.Count > 0) General.MainWindow.ShowEditThings(things);
				else return;

				General.Map.Map.Update();
				Setup();
				MapEdited?.Invoke(this, EventArgs.Empty);
			}
		}

		#endregion
	}
}
