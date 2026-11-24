using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Types;

namespace Builder.Tests
{
	public class LightmapLightingTests
	{
		// key, type, value, key, type, value...
		private static UniFields Fields(params object[] keys)
		{
			var fields = new UniFields();
			for(int i = 0; i < keys.Length; i += 3) fields.Add((string)keys[i], new UniValue((UniversalType)keys[i + 1], keys[i + 2]));
			return fields;
		}

		[Fact]
		public void RegistryDefaultsHaveTheTypesTheDialogStores()
		{
			// The dialog compares boxed values, so an int default on a float row would always read as changed
			foreach(LightmapBakeOption o in LightmapLighting.Options)
			{
				switch(o.Type)
				{
					case LightmapOptionType.Int: Assert.True(o.Default is int, o.Id); break;
					case LightmapOptionType.Float: Assert.True(o.Default is double, o.Id); break;
					case LightmapOptionType.Bool: Assert.True(o.Default is bool, o.Id); break;
					case LightmapOptionType.Colour: Assert.True(o.Default is string, o.Id); break;
					default: Assert.True(false, o.Id + " has a type the lighting edit can't write"); break;
				}
			}
		}

		[Fact]
		public void NoInfoThingReadsTheBakerDefaults()
		{
			LightmapLighting l = LightmapLighting.Read(null, 0, 0);
			Assert.False(l.HasThing);
			Assert.Equal(LightmapLighting.DEFAULT_ANGLE, l.Angle);
			Assert.Equal(LightmapLighting.DEFAULT_ELEVATION, l.Elevation);
			Assert.Equal(true, l["sun"]);
			foreach(LightmapBakeOption o in LightmapLighting.Options) Assert.Equal(o.Default, l[o.Id]);
			Assert.False(l.Clone().DiffersFrom(l));
		}

		[Fact]
		public void ReadConvertsTheThingsKeys()
		{
			var fields = Fields("lm_gridsize", UniversalType.Float, 64, "lm_sampledistance", UniversalType.Integer, 16,
				"lm_suncolor", UniversalType.String, "\"FF8000\"", "lm_sunintensity", UniversalType.Float, 0.0,
				"lm_ao", UniversalType.Boolean, false, "lm_skycolor", UniversalType.String, "not a colour");
			LightmapLighting l = LightmapLighting.Read(fields, 450, 330);
			Assert.True(l.HasThing);
			Assert.Equal(64.0, l["gridsize"]);
			Assert.Equal(16, l["sampledistance"]);
			Assert.Equal("ff8000", l["suncolor"]);
			Assert.Equal(false, l["ao"]);
			Assert.Equal("ffffff", l["skycolor"]); // unreadable: the default shows
			Assert.Equal(90, l.Angle);
			Assert.Equal(-30, l.Elevation);

			// Intensity 0 is the sun off; the intensity row offers the default to switch it back on with
			Assert.Equal(false, l["sun"]);
			Assert.Equal(1.0, l["sunintensity"]);
		}

		[Fact]
		public void WriteTouchesOnlyChangedKeysAndDropsDefaults()
		{
			var fields = Fields("lm_gridsize", UniversalType.Float, 64.0, "lm_sampledistance", UniversalType.Integer, 4, "user_note", UniversalType.String, "x");
			LightmapLighting original = LightmapLighting.Read(fields, 0, 0);
			LightmapLighting edited = original.Clone();
			edited["gridsize"] = 32.0;  // the default: removed
			edited["bounce"] = 3;
			edited["ao"] = false;
			edited["suncolor"] = "80a0ff";
			edited.WriteFields(fields, original);

			Assert.False(fields.ContainsKey("lm_gridsize"));
			Assert.Equal(3, fields["lm_bounce"].Value);
			Assert.Equal(false, fields["lm_ao"].Value);
			Assert.Equal((int)UniversalType.Boolean, fields["lm_ao"].Type);
			Assert.Equal("80a0ff", fields["lm_suncolor"].Value);
			Assert.Equal(4, fields["lm_sampledistance"].Value); // out of the row's range but not edited: kept
			Assert.Equal("x", fields["user_note"].Value);
			Assert.False(fields.ContainsKey("lm_sunintensity"));

			LightmapLighting back = LightmapLighting.Read(fields, 0, 0);
			LightmapLighting reverted = back.Clone();
			reverted["ao"] = true;
			reverted["bounce"] = 0;
			reverted.WriteFields(fields, back);
			Assert.False(fields.ContainsKey("lm_ao"));
			Assert.False(fields.ContainsKey("lm_bounce"));
		}

		[Fact]
		public void SunOffIsIntensityZero()
		{
			var fields = new UniFields();
			LightmapLighting original = LightmapLighting.Read(fields, 0, 0);
			LightmapLighting off = original.Clone();
			off["sun"] = false;
			off.WriteFields(fields, original);
			Assert.Equal(0.0, fields["lm_sunintensity"].Value);

			LightmapLighting read = LightmapLighting.Read(fields, 0, 0);
			LightmapLighting brighter = read.Clone();
			brighter["sun"] = true;
			brighter.WriteFields(fields, read);
			Assert.False(fields.ContainsKey("lm_sunintensity")); // back on at the default 1

			read = LightmapLighting.Read(fields, 0, 0);
			brighter = read.Clone();
			brighter["sunintensity"] = 2.5;
			brighter.WriteFields(fields, read);
			Assert.Equal(2.5, fields["lm_sunintensity"].Value);

			// An intensity edit while the sun stays off is no change at all
			read = LightmapLighting.Read(Fields("lm_sunintensity", UniversalType.Float, 0.0), 0, 0);
			LightmapLighting dimmed = read.Clone();
			dimmed["sunintensity"] = 2.5;
			Assert.False(dimmed.DiffersFrom(read));

			// Turning the sun off needs a baker that reads the intensity key
			LightmapBakeOption intensity = LightmapBakeOptions.Get("sunintensity");
			Assert.False(intensity.Available(LightmapBakerFeatures.None));
			Assert.True(intensity.Available(LightmapBakerFeatures.Parse("zdray-features 1\nsunintensity\n")));
		}

		[Fact]
		public void TheLastInfoThingIsTheOneUsed()
		{
			int count;
			Assert.Null(LightmapLighting.FindInfoThing(new[] { "1", "3004" }, int.Parse, out count));
			Assert.Equal(0, count);
			string[] things = { "9890", "1", "9890", "3004" };
			Assert.Same(things[2], LightmapLighting.FindInfoThing(things, int.Parse, out count));
			Assert.Equal(2, count);
		}

		[Fact]
		public void ElevationIsTheSignedPitch()
		{
			Assert.Equal(59, LightmapLighting.PitchToElevation(59));
			Assert.Equal(-30, LightmapLighting.PitchToElevation(330));
			Assert.Equal(-30, LightmapLighting.PitchToElevation(-30));
			Assert.Equal(0, LightmapLighting.PitchToElevation(360));
		}

		[Fact]
		public void ColoursNormalise()
		{
			Assert.Equal("ff8000", LightmapLighting.NormalizeColour("#FF8000"));
			Assert.Equal("ff8000", LightmapLighting.NormalizeColour(" \"ff8000\" "));
			Assert.Null(LightmapLighting.NormalizeColour("ff80"));
			Assert.Null(LightmapLighting.NormalizeColour("gg8000"));
			Assert.Null(LightmapLighting.NormalizeColour(null));
		}
	}
}
