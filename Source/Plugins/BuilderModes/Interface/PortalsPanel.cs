using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Portals;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder.BuilderModes
{
	public partial class PortalsPanel : UserControl
	{
		private const int IMAGE_KIND = 0, IMAGE_OK = 1, IMAGE_WARNING = 2, IMAGE_ERROR = 3, IMAGE_ELEMENT = 4;

		// Off unless UDB_PORTAL_LOG is set to a non-empty value other than "0"
		private static readonly bool logrefresh = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UDB_PORTAL_LOG"))
			&& Environment.GetEnvironmentVariable("UDB_PORTAL_LOG") != "0";

		private static readonly PortalKind[] kindorder =
		{
			PortalKind.LinePortal, PortalKind.LinkedLineEE, PortalKind.QuickPortal, PortalKind.AnchoredSector,
			PortalKind.StackedSectors, PortalKind.Skybox, PortalKind.SkyPicker, PortalKind.PlaneHorizon, PortalKind.Copy,
			PortalKind.Ignored,
		};

		private sealed class SectorList
		{
			public readonly List<int> Sectors;
			public SectorList(List<int> sectors) { Sectors = sectors; }
		}

		private readonly List<Bitmap> imagebitmaps = new List<Bitmap>();
		private Docker docker;
		private Form parentform;
		private PortalCapabilities caps = PortalCapabilities.None;
		private string selectedkey;
		private bool loading;

		public PortalCapabilities Capabilities { get { return caps; } set { caps = value ?? PortalCapabilities.None; } }

		public PortalsPanel()
		{
			InitializeComponent();
			BuildImages();
			loading = true;
			selectonclick.Checked = General.Settings.ReadPluginSetting("portalsselectonclick", true);
			centeronclick.Checked = General.Settings.ReadPluginSetting("portalscenteronclick", true);
			problemsonly.Checked = General.Settings.ReadPluginSetting("portalsproblemsonly", false);
			loading = false;
		}

		#region ================== Lifetime

		public void Setup(Docker owner)
		{
			docker = owner;
			parentform = ParentForm;
			if(parentform != null) parentform.Activated += parentform_Activated;
			if(Visible) UpdateTree();
		}

		public void Terminate()
		{
			updatetimer.Stop();
			if(parentform != null) parentform.Activated -= parentform_Activated;
			parentform = null;
		}

		// Refreshes shortly, so a burst of edits costs one analysis
		public void UpdateSoon()
		{
			updatetimer.Stop();
			updatetimer.Start();
		}

		private bool IsActiveTab
		{
			get { return docker != null && General.Interface.ActiveDockerTabName == docker.Title; }
		}

		#endregion

		#region ================== Tree

		private void UpdateTree()
		{
			if(General.Map == null || General.Map.Map == null || !General.Map.Map.IsSafeToAccess) return;

			Stopwatch watch = Stopwatch.StartNew();
			PortalMapData data = PortalMapSource.FromMap(General.Map.Map, caps);
			PortalReport report = PortalAnalyzer.Analyze(data, caps);
			long analysis = watch.ElapsedMilliseconds;

			// Keep the user's place: collapsed kinds, expanded links and the first visible row
			HashSet<PortalKind> collapsed = new HashSet<PortalKind>();
			HashSet<string> expanded = new HashSet<string>();
			string topkey = NodeKey(tree.TopNode);
			foreach(TreeNode kn in tree.Nodes)
			{
				if(kn.Tag is PortalKind && !kn.IsExpanded) collapsed.Add((PortalKind)kn.Tag);
				foreach(TreeNode ln in kn.Nodes)
					if(ln.IsExpanded && ln.Tag is PortalLink) expanded.Add(((PortalLink)ln.Tag).Key);
			}

			TreeNode selected = null, top = null;
			int shown = 0, errors = 0, warnings = 0;
			tree.BeginUpdate();
			try
			{
				tree.Nodes.Clear();
				foreach(PortalKind kind in kindorder)
				{
					List<PortalLink> links = report.Links.FindAll(l => l.Kind == kind && (!problemsonly.Checked || l.Issues.Count > 0));
					if(links.Count == 0) continue;

					TreeNode kindnode = new TreeNode(KindTitle(kind) + " (" + links.Count + ")", IMAGE_KIND, IMAGE_KIND) { Tag = kind };
					if(topkey != null && NodeKey(kindnode) == topkey) top = kindnode;
					int worst = IMAGE_OK;
					foreach(PortalLink link in links)
					{
						TreeNode node = LinkNode(link, data);
						kindnode.Nodes.Add(node);
						worst = Math.Max(worst, node.ImageIndex);
						if(link.Key == selectedkey) selected = node;
						if(link.Key == topkey) top = node;
						if(expanded.Contains(link.Key)) node.Expand();
						if(link.HasErrors) errors++;
						else if(link.HasWarnings) warnings++;
					}
					kindnode.ImageIndex = kindnode.SelectedImageIndex = worst == IMAGE_OK ? IMAGE_KIND : worst;
					tree.Nodes.Add(kindnode);

					// Expanding a child row expands its kind on Mono, so a collapsed kind is collapsed again here
					if(collapsed.Contains(kind)) kindnode.Collapse(true);
					else kindnode.Expand();
					shown += links.Count;
				}

				if(tree.Nodes.Count == 0)
					tree.Nodes.Add(new TreeNode(problemsonly.Checked && report.Links.Count > 0 ? "No portal problems found" : "No portals in this map", IMAGE_KIND, IMAGE_KIND));
				// Selecting a row inside a collapsed kind would expand it again, so the kind row takes the selection
				if(selected != null && selected.Parent != null && !selected.Parent.IsExpanded) selected = selected.Parent;
				if(selected != null) tree.SelectedNode = selected;
			}
			finally
			{
				tree.EndUpdate();
			}
			if(top != null) tree.TopNode = top;

			summary.Text = report.Links.Count + (report.Links.Count == 1 ? " portal" : " portals")
				+ (errors > 0 ? ", " + errors + " with errors" : "") + (warnings > 0 ? ", " + warnings + " with warnings" : "");

			if(logrefresh)
				General.WriteLogLine("Portals docker refresh: " + watch.ElapsedMilliseconds + " ms (analysis " + analysis + " ms, "
					+ data.Lines.Count + " lines, " + data.Sectors.Count + " sectors, " + data.Things.Count + " things, " + report.Links.Count + " links)");
		}

		private TreeNode LinkNode(PortalLink link, PortalMapData data)
		{
			int image = link.HasErrors ? IMAGE_ERROR : link.HasWarnings ? IMAGE_WARNING : IMAGE_OK;
			// Classic modes select one element type, so a line-to-thing row selects its line and centers on both
			bool mixed = link.Origin != null && link.Destination != null && link.Origin.Element.Type != link.Destination.Element.Type;
			string tip = mixed ? "Click to select the line and center on both ends. Right-click an end to edit it."
				: "Click to select both ends. Right-click an end to edit it.";
			TreeNode node = new TreeNode(LinkText(link), image, image) { Tag = link, ToolTipText = tip };

			if(link.Origin != null) node.Nodes.Add(EndNode(link.Origin, data));
			if(link.Destination != null) node.Nodes.Add(EndNode(link.Destination, data));
			else if(link.Kind != PortalKind.PlaneHorizon && link.Kind != PortalKind.Copy && link.Kind != PortalKind.Ignored) node.Nodes.Add(new TreeNode("Missing end", IMAGE_ELEMENT, IMAGE_ELEMENT));
			foreach(PortalEnd dependent in link.Dependents) node.Nodes.Add(EndNode(dependent, data));
			if(link.Sectors.Count > 0 && link.Kind != PortalKind.LinePortal && link.Kind != PortalKind.QuickPortal && link.Kind != PortalKind.LinkedLineEE)
				node.Nodes.Add(new TreeNode(SectorsText(link.Sectors), IMAGE_ELEMENT, IMAGE_ELEMENT) { Tag = new SectorList(link.Sectors), ToolTipText = "Click to select these sectors" });
			foreach(PortalIssue issue in link.Issues)
			{
				int issueimage = issue.Severity == PortalSeverity.Error ? IMAGE_ERROR : IMAGE_WARNING;
				node.Nodes.Add(new TreeNode((issue.Severity == PortalSeverity.Error ? "Error: " : "Warning: ") + issue.Message, issueimage, issueimage) { Tag = issue, ToolTipText = issue.Message });
			}
			return node;
		}

		private static TreeNode EndNode(PortalEnd end, PortalMapData data)
		{
			string text = end.Role + ": " + end.Element;
			if(end.Element.Type == PortalElementType.Linedef && end.Element.Index < data.Lines.Count)
			{
				PortalLine l = data.Lines[end.Element.Index];
				if(l.Front >= 0) text += " (front sector " + l.Front + ")";
			}
			else if(end.Element.Type == PortalElementType.Thing && end.Element.Index < data.Things.Count)
			{
				PortalThing t = data.Things[end.Element.Index];
				if(t.Sector >= 0) text += " (sector " + t.Sector + ")";
			}
			return new TreeNode(text, IMAGE_ELEMENT, IMAGE_ELEMENT) { Tag = end, ToolTipText = "Click to select. Right-click to edit." };
		}

		// Only kind and link rows have a key; the first visible row is restored only when it is one of them
		private static string NodeKey(TreeNode node)
		{
			if(node == null) return null;
			if(node.Tag is PortalKind) return "kind:" + node.Tag;
			if(node.Tag is PortalLink) return ((PortalLink)node.Tag).Key;
			return null;
		}

		private static string KindTitle(PortalKind kind)
		{
			switch(kind)
			{
				case PortalKind.LinePortal: return "Line portals";
				case PortalKind.LinkedLineEE: return "Linked line portals (Eternity style)";
				case PortalKind.QuickPortal: return "Quick portals";
				case PortalKind.AnchoredSector: return "Anchored sector portals";
				case PortalKind.StackedSectors: return "Stacked sectors";
				case PortalKind.Skybox: return "Skybox portals";
				case PortalKind.SkyPicker: return "Skybox pickers";
				case PortalKind.PlaneHorizon: return "Plane and horizon portals";
				case PortalKind.Ignored: return "Portal lines the engine ignores";
				default: return "Portal copies without a source";
			}
		}

		private static string LinkText(PortalLink link)
		{
			List<string> parts = new List<string>();
			switch(link.Kind)
			{
				case PortalKind.LinePortal:
					parts.Add(LineTypeName(link.Type));
					parts.Add(Pair(link));
					parts.Add("exit id " + link.Id);
					break;
				case PortalKind.LinkedLineEE:
					parts.Add("Linked");
					parts.Add(Pair(link));
					parts.Add("id " + link.Id);
					break;
				case PortalKind.QuickPortal:
					parts.Add(link.Type == 3 ? "Linked" : "Visual");
					parts.Add(Pair(link));
					parts.Add("id " + link.Id);
					break;
				case PortalKind.AnchoredSector:
					parts.Add((link.Type == 6 ? "Linked " : "Visual ") + PlaneName(link.Plane));
					parts.Add(Pair(link));
					parts.Add("tag " + link.Id);
					break;
				case PortalKind.StackedSectors:
					parts.Add(Pair(link));
					parts.Add("TID " + link.Id);
					break;
				case PortalKind.Skybox:
					parts.Add("Skybox " + PlaneName(link.Plane));
					parts.Add(Pair(link));
					parts.Add("tag " + link.Id);
					break;
				case PortalKind.SkyPicker:
					parts.Add(Pair(link));
					parts.Add("TID " + link.Id);
					break;
				case PortalKind.PlaneHorizon:
					parts.Add((link.Type == 3 ? "Plane " : "Horizon ") + PlaneName(link.Plane));
					parts.Add(link.Origin.Element.ToString());
					parts.Add("tag " + link.Id);
					break;
				case PortalKind.Ignored:
					parts.Add(link.Origin.Role);
					parts.Add(link.Origin.Element.ToString());
					parts.Add("type " + link.Type);
					break;
				default:
					parts.Add(link.Origin.Role);
					parts.Add(link.Origin.Element.ToString());
					parts.Add("source tag " + link.Id);
					break;
			}
			if(link.HasDisplacement)
				parts.Add("offset " + link.Displacement.x.ToString("0.###", CultureInfo.InvariantCulture) + ", " + link.Displacement.y.ToString("0.###", CultureInfo.InvariantCulture));
			return string.Join(" · ", parts);
		}

		private static string Pair(PortalLink link)
		{
			string a = link.Origin != null ? link.Origin.Element.ToString() : "(missing)";
			string b = link.Destination != null ? link.Destination.Element.ToString() : "(missing)";
			return a + (link.TwoWay ? " <-> " : " -> ") + b;
		}

		private static string LineTypeName(int type)
		{
			switch(type)
			{
				case 0: return "Visual";
				case 1: return "Teleport";
				case 2: return "Interactive";
				case 3: return "Linked";
				default: return "Type " + type;
			}
		}

		private static string PlaneName(int plane)
		{
			switch(plane)
			{
				case 0: return "floor";
				case 1: return "ceiling";
				case 2: return "floor and ceiling";
				case 3: return "any plane";
				default: return "plane " + plane;
			}
		}

		private static string SectorsText(List<int> sectors)
		{
			const int max = 12;
			List<string> names = new List<string>();
			for(int i = 0; i < sectors.Count && i < max; i++) names.Add(sectors[i].ToString(CultureInfo.InvariantCulture));
			return (sectors.Count == 1 ? "Sector " : "Sectors ") + string.Join(", ", names) + (sectors.Count > max ? " and " + (sectors.Count - max) + " more" : "");
		}

		// The bitmaps are kept and disposed after the image list, since Mono's ImageList may still reference them
		private void BuildImages()
		{
			imagebitmaps.Add(Dot(Color.FromArgb(128, 128, 128), true));
			imagebitmaps.Add(Dot(Color.FromArgb(64, 170, 80), false));
			imagebitmaps.Add(Dot(Color.FromArgb(230, 160, 20), false));
			imagebitmaps.Add(Dot(Color.FromArgb(215, 50, 50), false));
			imagebitmaps.Add(new Bitmap(16, 16));
			foreach(Bitmap b in imagebitmaps) images.Images.Add(b);
		}

		private static Bitmap Dot(Color color, bool square)
		{
			Bitmap b = new Bitmap(16, 16);
			using(Graphics g = Graphics.FromImage(b))
			using(SolidBrush brush = new SolidBrush(color))
			{
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				if(square) g.FillRectangle(brush, 4, 4, 8, 8);
				else g.FillEllipse(brush, 3, 3, 10, 10);
			}
			return b;
		}

		#endregion

		#region ================== Selection

		private static List<PortalElement> ElementsOf(object tag)
		{
			List<PortalElement> result = new List<PortalElement>();
			if(tag is PortalLink)
				foreach(PortalEnd e in ((PortalLink)tag).Ends) result.Add(e.Element);
			else if(tag is PortalEnd)
				result.Add(((PortalEnd)tag).Element);
			else if(tag is PortalIssue)
				result.AddRange(((PortalIssue)tag).Elements);
			else if(tag is SectorList)
				foreach(int s in ((SectorList)tag).Sectors) result.Add(new PortalElement(PortalElementType.Sector, s));
			return result;
		}

		private static void SelectElements(List<PortalElement> elements)
		{
			General.Editing.CancelVolatileMode();
			General.Map.Map.ClearAllSelected();

			PortalElementType type = elements[0].Type;
			string mode = type == PortalElementType.Thing ? "ThingsMode" : type == PortalElementType.Sector ? "SectorsMode" : "LinedefsMode";
			if(General.Editing.Mode == null || General.Editing.Mode.GetType().Name != mode) General.Editing.ChangeMode(mode);
			ClassicMode classic = General.Editing.Mode as ClassicMode;

			List<Sector> sectors = new List<Sector>();
			foreach(PortalElement e in elements)
			{
				if(e.Type != type) continue;
				switch(e.Type)
				{
					case PortalElementType.Linedef:
						Linedef l = General.Map.Map.GetLinedefByIndex(e.Index);
						if(l != null) l.Selected = true;
						break;
					case PortalElementType.Thing:
						Thing t = General.Map.Map.GetThingByIndex(e.Index);
						if(t != null) t.Selected = true;
						break;
					default:
						Sector s = General.Map.Map.GetSectorByIndex(e.Index);
						if(s != null) sectors.Add(s);
						break;
				}
			}

			if(sectors.Count == 0) return;
			if(classic is SectorsMode) ((SectorsMode)classic).SelectSectors(sectors);
			else if(classic != null) foreach(Sector s in sectors) classic.SelectMapElement(s);
		}

		private static void CenterOn(List<PortalElement> elements)
		{
			ClassicMode classic = General.Editing.Mode as ClassicMode;
			if(classic == null) return;

			RectangleF area = MapSet.CreateEmptyArea();
			bool any = false;
			foreach(PortalElement e in elements)
			{
				switch(e.Type)
				{
					case PortalElementType.Linedef:
						Linedef l = General.Map.Map.GetLinedefByIndex(e.Index);
						if(l == null) break;
						area = MapSet.IncreaseArea(area, l.Start.Position);
						area = MapSet.IncreaseArea(area, l.End.Position);
						any = true;
						break;
					case PortalElementType.Thing:
						Thing t = General.Map.Map.GetThingByIndex(e.Index);
						if(t == null) break;
						Vector2D p = t.Position;
						area = MapSet.IncreaseArea(area, p + new Vector2D(t.Size * 2, t.Size * 2));
						area = MapSet.IncreaseArea(area, p - new Vector2D(t.Size * 2, t.Size * 2));
						any = true;
						break;
					default:
						Sector s = General.Map.Map.GetSectorByIndex(e.Index);
						if(s == null) break;
						foreach(Sidedef sd in s.Sidedefs)
						{
							area = MapSet.IncreaseArea(area, sd.Line.Start.Position);
							area = MapSet.IncreaseArea(area, sd.Line.End.Position);
						}
						any = true;
						break;
				}
			}
			if(!any) return;

			// Square the area on its larger side, as Tag Explorer does
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
			classic.CenterOnArea(area, 0.6f);
		}

		private static bool EditElement(PortalElement e)
		{
			switch(e.Type)
			{
				case PortalElementType.Linedef:
					Linedef l = General.Map.Map.GetLinedefByIndex(e.Index);
					return l != null && General.Interface.ShowEditLinedefs(new List<Linedef> { l }) == DialogResult.OK;
				case PortalElementType.Thing:
					Thing t = General.Map.Map.GetThingByIndex(e.Index);
					return t != null && General.Interface.ShowEditThings(new List<Thing> { t }) == DialogResult.OK;
				default:
					Sector s = General.Map.Map.GetSectorByIndex(e.Index);
					return s != null && General.Interface.ShowEditSectors(new List<Sector> { s }) == DialogResult.OK;
			}
		}

		#endregion

		#region ================== Events

		private void tree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
		{
			if(General.Map == null) return;
			tree.SelectedNode = e.Node;
			PortalLink link = e.Node.Tag as PortalLink ?? (e.Node.Parent != null ? e.Node.Parent.Tag as PortalLink : null);
			if(link != null) selectedkey = link.Key;

			if(e.Button == MouseButtons.Right)
			{
				if(e.Node.Tag is PortalEnd && EditElement(((PortalEnd)e.Node.Tag).Element))
				{
					General.Map.Map.Update();
					General.Interface.RedrawDisplay();
					UpdateTree();
				}
				return;
			}

			List<PortalElement> elements = ElementsOf(e.Node.Tag);
			if(elements.Count == 0) return;
			if(selectonclick.Checked) SelectElements(elements);
			if(centeronclick.Checked) CenterOn(elements);
			if(General.Editing.Mode != null) General.Editing.Mode.UpdateSelectionInfo();
			General.Interface.RedrawDisplay();
		}

		private void option_CheckedChanged(object sender, EventArgs e)
		{
			if(loading) return;
			General.Settings.WritePluginSetting("portalsselectonclick", selectonclick.Checked);
			General.Settings.WritePluginSetting("portalscenteronclick", centeronclick.Checked);
		}

		private void problemsonly_CheckedChanged(object sender, EventArgs e)
		{
			if(loading) return;
			General.Settings.WritePluginSetting("portalsproblemsonly", problemsonly.Checked);
			UpdateTree();
		}

		// Activated fires when a properties dialog closes, which edit accept does not report
		private void parentform_Activated(object sender, EventArgs e)
		{
			UpdateSoon();
		}

		private void updatetimer_Tick(object sender, EventArgs e)
		{
			updatetimer.Stop();
			if(General.Map == null || !IsActiveTab) return;
			UpdateTree();
		}

		private void PortalsPanel_VisibleChanged(object sender, EventArgs e)
		{
			if(Visible && docker != null) UpdateTree();
		}

		#endregion
	}
}
