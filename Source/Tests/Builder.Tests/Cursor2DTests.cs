using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;
using Xunit;

namespace Builder.Tests
{
	public class Cursor2DTests
	{
		// Write, serialize to text and parse back, the way a .dbs round-trips
		private static Configuration RoundTrip(Cursor2D cursor)
		{
			Configuration cfg = new Configuration(true);
			cursor.WriteToConfig(cfg, "cursor2d");
			Configuration back = new Configuration(true);
			Assert.True(back.InputConfiguration(cfg.OutputConfiguration()));
			return back;
		}

		[Fact]
		public void UnsetRoundTripsAsUnset()
		{
			Cursor2D read = new Cursor2D();
			read.Place(new Vector2D(5, 5), 0);
			read.ReadFromConfig(RoundTrip(new Cursor2D()), "cursor2d");
			Assert.False(read.IsSet);
		}

		[Fact]
		public void SetPointRoundTrips()
		{
			Cursor2D cursor = new Cursor2D();
			cursor.Place(new Vector2D(-123.257, 64.5), 3);
			Cursor2D read = new Cursor2D();
			read.ReadFromConfig(RoundTrip(cursor), "cursor2d");
			Assert.True(read.IsSet);
			Assert.Equal(new Vector2D(-123.257, 64.5), read.Position);

			cursor.Place(new Vector2D(96, -32), 0);
			read.ReadFromConfig(RoundTrip(cursor), "cursor2d");
			Assert.Equal(new Vector2D(96, -32), read.Position);
		}

		[Fact]
		public void EmptyConfigClearsASetCursorWithoutChangedButBumpsVersion()
		{
			Cursor2D cursor = new Cursor2D();
			cursor.Place(new Vector2D(1, 2), 0);
			int raised = 0;
			cursor.Changed += delegate { raised++; };
			int version = cursor.Version;

			cursor.ReadFromConfig(new Configuration(true), "cursor2d");

			Assert.False(cursor.IsSet);
			Assert.Equal(0, raised);
			Assert.True(cursor.Version > version);
		}

		[Fact]
		public void PlaceRoundsAndRejectsNonFinite()
		{
			Cursor2D cursor = new Cursor2D();
			cursor.Place(new Vector2D(1.23456, -7.0006), 3);
			Assert.Equal(new Vector2D(1.235, -7.001), cursor.Position);
			cursor.Place(new Vector2D(10.6, -3.4), 0);
			Assert.Equal(new Vector2D(11, -3), cursor.Position);

			int version = cursor.Version;
			Assert.Throws<ArgumentException>(() => cursor.Place(new Vector2D(double.NaN, 0), 3));
			Assert.Throws<ArgumentException>(() => cursor.Place(new Vector2D(0, double.PositiveInfinity), 3));
			Assert.True(cursor.IsSet);
			Assert.Equal(new Vector2D(11, -3), cursor.Position);
			Assert.Equal(version, cursor.Version);
		}

		[Fact]
		public void PlaceAndClearBumpVersionAndRaiseChanged()
		{
			Cursor2D cursor = new Cursor2D();
			int raised = 0;
			cursor.Changed += delegate { raised++; };
			int v0 = cursor.Version;
			cursor.Place(new Vector2D(0, 0), 0);
			Assert.True(cursor.Version > v0);
			int v1 = cursor.Version;
			cursor.Clear();
			Assert.True(cursor.Version > v1);
			Assert.False(cursor.IsSet);
			Assert.Equal(2, raised);
		}

		[Fact]
		public void SelectionCenterPadsThingsBySize()
		{
			Vector2D center;
			List<KeyValuePair<Vector2D, double>> nothings = new List<KeyValuePair<Vector2D, double>>();

			Assert.False(Cursor2D.TryGetSelectionCenter(new Vector2D[0], nothings, out center));

			Assert.True(Cursor2D.TryGetSelectionCenter(new[] { new Vector2D(13, -7) }, nothings, out center));
			Assert.Equal(new Vector2D(13, -7), center);

			Assert.True(Cursor2D.TryGetSelectionCenter(new Vector2D[0],
				new[] { new KeyValuePair<Vector2D, double>(new Vector2D(40, 8), 20) }, out center));
			Assert.Equal(new Vector2D(40, 8), center);

			// Vertices span x 0..64; the thing at x 64 pads the right edge to 64 + 32
			Assert.True(Cursor2D.TryGetSelectionCenter(new[] { new Vector2D(0, 0), new Vector2D(64, 64) },
				new[] { new KeyValuePair<Vector2D, double>(new Vector2D(64, 32), 32) }, out center));
			Assert.Equal(new Vector2D(48, 32), center);
		}
	}
}
