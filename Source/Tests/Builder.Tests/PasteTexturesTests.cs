using System.Collections;
using System.Reflection;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder.Map;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class PasteTexturesTests
	{
		private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

		[Fact]
		public void TwoSidedSourceFillsOnlyRequiredOrTexturedPartsAndSkipsUnchanged()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				LinedefProperties source = Source(Side("UP", "-", "LO"), Side("UP2", "-", "LO2"));
				// Front faces the higher ceiling, so only its upper is required; the lower is neither required nor textured
				Linedef target = TwoSided(out Sidedef front, out Sidedef back, 128, 0, 64, 0);
				SetTextures(front, "OLDUP", "-", "-");
				SetTextures(back, "-", "-", "-");
				Assert.Equal(new[] { Change(front, "Upper", "UP") }, Plan(source, target));

				SetTextures(front, "UP", "-", "-");
				Assert.Empty(Plan(source, target));
			}
		}

		[Fact]
		public void SingleSidedSourceOnlyFillsSingleSidedTargets()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				LinedefProperties source = Source(Side("-", "WALL", "-"), null);
				Linedef twosided = TwoSided(out Sidedef front, out Sidedef back, 128, 0, 64, 32);
				SetTextures(front, "A", "-", "B");
				SetTextures(back, "-", "-", "-");
				Assert.Empty(Plan(source, twosided));

				Linedef onesided = OneSided(out Sidedef side);
				SetTextures(side, "-", "OLD", "-");
				Assert.Equal(new[] { Change(side, "Middle", "WALL") }, Plan(source, onesided));
			}
		}

		[Fact]
		public void TwoSidedSourceWithNoMiddleLeavesSingleSidedTargets()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				LinedefProperties source = Source(Side("UP", "-", "LO"), Side("UP", "-", "LO"));
				Linedef onesided = OneSided(out Sidedef side);
				SetTextures(side, "-", "OLD", "-");
				Assert.Empty(Plan(source, onesided));
			}
		}

		[Fact]
		public void GrateMiddleReplacesOnlyAnExistingTwoSidedMiddle()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				LinedefProperties source = Source(Side("-", "GRATE", "-"), Side("-", "GRATE2", "-"));
				Linedef target = TwoSided(out Sidedef front, out Sidedef back, 128, 0, 128, 0);
				SetTextures(front, "-", "OLDGRATE", "-");
				SetTextures(back, "-", "-", "-");
				Assert.Equal(new[] { Change(front, "Middle", "GRATE") }, Plan(source, target));
			}
		}

		[Fact]
		public void SourceWithoutBackLeavesBackSidesAlone()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				LinedefProperties source = Source(Side("UP", "MID", "LO"), null);
				Linedef target = TwoSided(out Sidedef front, out Sidedef back, 64, 32, 128, 0);
				SetTextures(front, "-", "-", "-");
				SetTextures(back, "OLDUP", "-", "OLDLO");
				Assert.Empty(Plan(source, target));
			}
		}

		[Fact]
		public void SectorsTakeBothFlatsSkippingDashAndUnchanged()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				Sector target = Uninitialized.Create<Sector>();
				SetField(target, "floortexname", "OLDF");
				SetField(target, "ceiltexname", "OLDC");
				Assert.Equal(new[] { Change(target, "Floor", "FLAT1"), Change(target, "Ceiling", "CEIL1") }, Plan(Flats("FLAT1", "CEIL1"), target));
				Assert.Equal(new[] { Change(target, "Floor", "FLAT1") }, Plan(Flats("FLAT1", "-"), target));
				Assert.Empty(Plan(Flats("OLDF", "OLDC"), target));
			}
		}

		private static string Change(object target, string part, string name) { return target.GetHashCode() + ":" + part + ":" + name; }

		private static string[] Plan(object props, object target)
		{
			IList single = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(target.GetType()))!;
			single.Add(target);
			IEnumerable changes = (IEnumerable)props.GetType().GetMethod("PlanTextures", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(ICollection<>).MakeGenericType(target.GetType()) }, null)!.Invoke(props, new object[] { single })!;
			var result = new List<string>();
			foreach(object c in changes)
			{
				Type t = c.GetType();
				result.Add(Change(t.GetField("Target")!.GetValue(c)!, t.GetField("Part")!.GetValue(c)!.ToString()!, (string)t.GetField("Name")!.GetValue(c)!));
			}
			return result.ToArray();
		}

		private static SidedefProperties Side(string high, string mid, string low)
		{
			SidedefProperties s = Uninitialized.Create<SidedefProperties>();
			SetField(s, "hightexture", high);
			SetField(s, "middletexture", mid);
			SetField(s, "lowtexture", low);
			return s;
		}

		private static LinedefProperties Source(SidedefProperties front, SidedefProperties? back)
		{
			LinedefProperties l = Uninitialized.Create<LinedefProperties>();
			SetField(l, "front", front);
			SetField(l, "back", back);
			return l;
		}

		private static SectorProperties Flats(string floor, string ceiling)
		{
			SectorProperties s = Uninitialized.Create<SectorProperties>();
			SetField(s, "floortexture", floor);
			SetField(s, "ceilingtexture", ceiling);
			return s;
		}

		private static Linedef TwoSided(out Sidedef front, out Sidedef back, int frontceil, int frontfloor, int backceil, int backfloor)
		{
			Linedef line = Uninitialized.Create<Linedef>();
			front = MakeSide(line, MakeSector(frontceil, frontfloor));
			back = MakeSide(line, MakeSector(backceil, backfloor));
			SetField(line, "front", front);
			SetField(line, "back", back);
			return line;
		}

		private static Linedef OneSided(out Sidedef front)
		{
			Linedef line = Uninitialized.Create<Linedef>();
			front = MakeSide(line, MakeSector(128, 0));
			SetField(line, "front", front);
			return line;
		}

		private static Sector MakeSector(int ceil, int floor)
		{
			Sector s = Uninitialized.Create<Sector>();
			SetField(s, "ceilheight", ceil);
			SetField(s, "floorheight", floor);
			SetField(s, "floortexname", "FLOOR");
			SetField(s, "ceiltexname", "CEIL");
			return s;
		}

		private static Sidedef MakeSide(Linedef line, Sector sector)
		{
			Sidedef side = Uninitialized.Create<Sidedef>();
			SetField(side, "linedef", line);
			SetField(side, "sector", sector);
			return side;
		}

		private static void SetTextures(Sidedef side, string high, string mid, string low)
		{
			SetField(side, "texnamehigh", high);
			SetField(side, "texnamemid", mid);
			SetField(side, "texnamelow", low);
		}

		private static void SetField(object target, string name, object? value)
		{
			for(Type? t = target.GetType(); t != null; t = t.BaseType)
			{
				FieldInfo? f = t.GetField(name, Fields);
				if(f != null) { f.SetValue(target, value); return; }
			}
			throw new MissingFieldException(target.GetType().Name, name);
		}
	}
}
