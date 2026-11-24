#region ================== Namespaces

using System.Collections.Generic;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Portals;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	[ErrorChecker("Check portals", true, 50)]
	public class CheckPortals : ErrorChecker
	{
		#region ================== Variables

		private readonly PortalCapabilities caps;

		#endregion

		#region ================== Properties

		// Only ZDoom-family configurations, the same gate as the Portals docker
		public override bool SkipCheck { get { return !caps.Any; } }

		#endregion

		#region ================== Constructor / Destructor

		public CheckPortals()
		{
			caps = PortalMapSource.Capabilities(General.Map.Config, General.Map.FormatInterface);
			SetTotalProgress(1);
		}

		#endregion

		#region ================== Methods

		public override void Run()
		{
			PortalMapData data = PortalMapSource.FromMap(General.Map.Map, caps);
			PortalReport report = PortalAnalyzer.Analyze(data, caps);

			foreach(PortalIssue issue in report.Issues)
			{
				List<MapElement> elements = new List<MapElement>(issue.Elements.Count);
				foreach(PortalElement e in issue.Elements)
				{
					MapElement element = null;
					switch(e.Type)
					{
						case PortalElementType.Linedef: element = General.Map.Map.GetLinedefByIndex(e.Index); break;
						case PortalElementType.Sector: element = General.Map.Map.GetSectorByIndex(e.Index); break;
						case PortalElementType.Thing: element = General.Map.Map.GetThingByIndex(e.Index); break;
					}
					if(element != null && !elements.Contains(element)) elements.Add(element);
				}
				if(elements.Count > 0) SubmitResult(ResultPortalIssue.Create(issue, elements));
			}
			AddProgress(1);
		}

		#endregion
	}
}
