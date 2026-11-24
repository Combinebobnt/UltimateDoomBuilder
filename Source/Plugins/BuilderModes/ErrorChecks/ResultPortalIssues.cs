#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Portals;
using CodeImp.DoomBuilder.Rendering;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	// Hiding is keyed by result type on each element, so every issue code has its own sealed subclass below
	public abstract class ResultPortalIssue : ErrorResult
	{
		#region ================== Variables

		private readonly List<MapElement> elements;
		private readonly string text;

		#endregion

		#region ================== Properties

		public override int Buttons { get { return 0; } }

		#endregion

		#region ================== Constructor / Destructor

		protected ResultPortalIssue(PortalIssue issue, List<MapElement> elements)
		{
			this.elements = elements;
			this.hidden = true;
			foreach(MapElement e in elements)
			{
				this.viewobjects.Add(e);
				this.hidden &= e.IgnoredErrorChecks.Contains(this.GetType());
			}

			text = (issue.Severity == PortalSeverity.Error ? "Portal error: " : "Portal warning: ") + issue.Message;
			List<string> names = new List<string>(elements.Count);
			foreach(MapElement e in elements) names.Add(ElementName(e));
			description = issue.Message + ". Elements: " + string.Join(", ", names.ToArray()) + ".";
		}

		#endregion

		#region ================== Methods

		public static ResultPortalIssue Create(PortalIssue issue, List<MapElement> elements)
		{
			switch(issue.Code)
			{
				case PortalIssueCode.NoExit: return new ResultPortalNoExit(issue, elements);
				case PortalIssueCode.NoReturn: return new ResultPortalNoReturn(issue, elements);
				case PortalIssueCode.ReturnNotLinked: return new ResultPortalReturnNotLinked(issue, elements);
				case PortalIssueCode.AmbiguousExit: return new ResultPortalAmbiguousExit(issue, elements);
				case PortalIssueCode.OneSidedTraversable: return new ResultPortalOneSidedTraversable(issue, elements);
				case PortalIssueCode.InteractivePlaneAlign: return new ResultPortalInteractivePlaneAlign(issue, elements);
				case PortalIssueCode.LinkedGeometry: return new ResultPortalLinkedGeometry(issue, elements);
				case PortalIssueCode.LengthMismatch: return new ResultPortalLengthMismatch(issue, elements);
				case PortalIssueCode.SameGroup: return new ResultPortalSameGroup(issue, elements);
				case PortalIssueCode.DisplacementConflict: return new ResultPortalDisplacementConflict(issue, elements);
				case PortalIssueCode.UnknownType: return new ResultPortalUnknownType(issue, elements);
				case PortalIssueCode.TypeUnsupported: return new ResultPortalTypeUnsupported(issue, elements);
				case PortalIssueCode.ZeroSectorTag: return new ResultPortalZeroSectorTag(issue, elements);
				case PortalIssueCode.BadPlane: return new ResultPortalBadPlane(issue, elements);
				case PortalIssueCode.LoneAnchor: return new ResultPortalLoneAnchor(issue, elements);
				case PortalIssueCode.DuplicateAnchor: return new ResultPortalDuplicateAnchor(issue, elements);
				case PortalIssueCode.NoTaggedSector: return new ResultPortalNoTaggedSector(issue, elements);
				case PortalIssueCode.LinkedSloped: return new ResultPortalLinkedSloped(issue, elements);
				case PortalIssueCode.OpacityMismatch: return new ResultPortalOpacityMismatch(issue, elements);
				case PortalIssueCode.CopyNoSource: return new ResultPortalCopyNoSource(issue, elements);
				case PortalIssueCode.CopyToLineAnchored: return new ResultPortalCopyToLineAnchored(issue, elements);
				case PortalIssueCode.StackUnpaired: return new ResultPortalStackUnpaired(issue, elements);
				case PortalIssueCode.StackDuplicate: return new ResultPortalStackDuplicate(issue, elements);
				case PortalIssueCode.PickerNoViewpoint: return new ResultPortalPickerNoViewpoint(issue, elements);
				default: throw new NotImplementedException("No error result for portal issue " + issue.Code);
			}
		}

		private static string ElementName(MapElement e)
		{
			if(e is Linedef) return "linedef " + ((Linedef)e).Index;
			if(e is Sector) return "sector " + ((Sector)e).Index;
			if(e is Thing) return "thing " + ((Thing)e).Index;
			return e.ToString();
		}

		// This sets if this result is displayed in ErrorCheckForm
		internal override void Hide(bool hide)
		{
			hidden = hide;
			Type t = this.GetType();
			foreach(MapElement e in elements)
			{
				if(hide) e.IgnoredErrorChecks.Add(t);
				else if(e.IgnoredErrorChecks.Contains(t)) e.IgnoredErrorChecks.Remove(t);
			}
		}

		// This must return the string that is displayed in the listbox
		public override string ToString()
		{
			return text;
		}

		public override void PlotSelection(IRenderer2D renderer)
		{
			foreach(MapElement e in elements)
			{
				if(e is Linedef)
				{
					Linedef l = (Linedef)e;
					renderer.PlotLinedef(l, General.Colors.Selection);
					renderer.PlotVertex(l.Start, ColorCollection.VERTICES);
					renderer.PlotVertex(l.End, ColorCollection.VERTICES);
				}
				else if(e is Sector)
				{
					renderer.PlotSector((Sector)e, General.Colors.Selection);
				}
			}
		}

		public override void RenderOverlaySelection(IRenderer2D renderer)
		{
			foreach(MapElement e in elements)
				if(e is Thing) renderer.RenderThing((Thing)e, General.Colors.Selection, General.Settings.ActiveThingsAlpha);
		}

		#endregion
	}

	public sealed class ResultPortalNoExit : ResultPortalIssue { public ResultPortalNoExit(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalNoReturn : ResultPortalIssue { public ResultPortalNoReturn(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalReturnNotLinked : ResultPortalIssue { public ResultPortalReturnNotLinked(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalAmbiguousExit : ResultPortalIssue { public ResultPortalAmbiguousExit(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalOneSidedTraversable : ResultPortalIssue { public ResultPortalOneSidedTraversable(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalInteractivePlaneAlign : ResultPortalIssue { public ResultPortalInteractivePlaneAlign(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalLinkedGeometry : ResultPortalIssue { public ResultPortalLinkedGeometry(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalLengthMismatch : ResultPortalIssue { public ResultPortalLengthMismatch(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalSameGroup : ResultPortalIssue { public ResultPortalSameGroup(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalDisplacementConflict : ResultPortalIssue { public ResultPortalDisplacementConflict(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalUnknownType : ResultPortalIssue { public ResultPortalUnknownType(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalTypeUnsupported : ResultPortalIssue { public ResultPortalTypeUnsupported(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalZeroSectorTag : ResultPortalIssue { public ResultPortalZeroSectorTag(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalBadPlane : ResultPortalIssue { public ResultPortalBadPlane(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalLoneAnchor : ResultPortalIssue { public ResultPortalLoneAnchor(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalDuplicateAnchor : ResultPortalIssue { public ResultPortalDuplicateAnchor(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalNoTaggedSector : ResultPortalIssue { public ResultPortalNoTaggedSector(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalLinkedSloped : ResultPortalIssue { public ResultPortalLinkedSloped(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalOpacityMismatch : ResultPortalIssue { public ResultPortalOpacityMismatch(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalCopyNoSource : ResultPortalIssue { public ResultPortalCopyNoSource(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalCopyToLineAnchored : ResultPortalIssue { public ResultPortalCopyToLineAnchored(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalStackUnpaired : ResultPortalIssue { public ResultPortalStackUnpaired(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalStackDuplicate : ResultPortalIssue { public ResultPortalStackDuplicate(PortalIssue i, List<MapElement> e) : base(i, e) { } }
	public sealed class ResultPortalPickerNoViewpoint : ResultPortalIssue { public ResultPortalPickerNoViewpoint(PortalIssue i, List<MapElement> e) : base(i, e) { } }
}
