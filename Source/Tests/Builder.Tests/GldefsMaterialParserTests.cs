using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.ZDoom;
using System.Text;

namespace Builder.Tests
{
	public class GldefsMaterialParserTests
	{
		[Theory]
		[InlineData("", true)]
		[InlineData("gzdrawmaterials = false;", false)]
		[InlineData("gzdrawmaterials = true;", true)]
		public void MaterialSettingLoadsAndDefaultsOn(string setting, bool expected)
		{
			string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cfg");
			try
			{
				File.WriteAllText(path, "currentversion = 1;\n" + setting);
				var config = new CodeImp.DoomBuilder.Config.ProgramConfiguration();
				Assert.True(config.Load(path, path, path));
				Assert.Equal(expected, config.GZDrawMaterials);
			}
			finally { File.Delete(path); }
		}

		private static GldefsParser Parse(string text, out bool result)
		{
			var data = new TextResourceData(new MemoryStream(Encoding.UTF8.GetBytes(text)),
				new DataLocation(DataLocation.RESOURCE_DIRECTORY, string.Empty, false, false, false, new List<string>()), "GLDEFS");
			var parser = new GldefsParser();
			result = parser.Parse(data, true);
			return parser;
		}

		[Fact]
		public void GlowHeightsAreTheEnginesUnits()
		{
			var parser = Parse("Glow { Texture \"GLOWA\", 00FF00, 96\nTexture \"GLOWB\", FF0000, 128, fullbright\nFlats { GLOWC } }", out bool result);
			Assert.True(result);
			Assert.Equal(96.0, parser.GlowingFlats[CodeImp.DoomBuilder.IO.Lump.MakeLongName("GLOWA", true)].Height);
			Assert.Equal(128.0, parser.GlowingFlats[CodeImp.DoomBuilder.IO.Lump.MakeLongName("GLOWB", true)].Height);
			Assert.Equal(128.0, parser.GlowingFlats[CodeImp.DoomBuilder.IO.Lump.MakeLongName("GLOWC", true)].Height);
		}

		private sealed class TargetImage : ImageData
		{
			internal TargetImage(int type, bool sprite = false)
			{
				AnimationUseType = type;
				if(sprite) texturenamespace = TextureNamespace.SPRITE;
			}
			protected override LocalLoadResult LocalLoadImage() { throw new NotSupportedException(); }
		}

		[Fact]
		public void TargetLookupPrefersNamespaceThenOneFallback()
		{
			var flat = new TargetImage(1);
			var wall = new TargetImage(0);
			var sprite = new TargetImage(0, true);
			var candidates = new ImageData[] { sprite, flat, wall };
			Assert.Same(sprite, GldefsMaterial.SelectTarget(candidates, MaterialTarget.Any, false));
			Assert.Same(wall, GldefsMaterial.SelectTarget(candidates, MaterialTarget.Texture, false));
			Assert.Same(flat, GldefsMaterial.SelectTarget(candidates, MaterialTarget.Flat, false));
			Assert.Same(sprite, GldefsMaterial.SelectTarget(candidates, MaterialTarget.Sprite, false));
			Assert.Same(flat, GldefsMaterial.SelectTarget(new ImageData[] { flat }, MaterialTarget.Texture, false));
			Assert.Same(sprite, GldefsMaterial.SelectTarget(new ImageData[] { sprite }, MaterialTarget.Texture, false));
			Assert.Null(GldefsMaterial.SelectTarget(new ImageData[0], MaterialTarget.Any, false));
		}

		[Fact]
		public void RedefiningBrightmapResetsItsFlagWithoutChangingShader()
		{
			var material = new GldefsMaterial();
			material.Merge(new GldefsMaterial { Brightmap = "first", DisableFullbright = true });
			material.Merge(new GldefsMaterial { Shader = "shader" });
			Assert.True(material.DisableFullbright);
			material.Merge(new GldefsMaterial { Brightmap = "second" });
			Assert.False(material.DisableFullbright);
			Assert.Equal("shader", material.Shader);
		}

		[Fact]
		public void OverridableHonorsEncounterOrderInsteadOfSpreadingAssignments()
		{
			var wall = new TargetImage(0);
			var image = new TargetImage(2);
			Assert.Same(image, GldefsMaterial.SelectTarget(new ImageData[] { image, wall }, MaterialTarget.Texture, true));
			Assert.Same(wall, GldefsMaterial.SelectTarget(new ImageData[] { image, wall }, MaterialTarget.Texture, false));
			Assert.Same(wall, GldefsMaterial.SelectTarget(new ImageData[] { wall, image }, MaterialTarget.Texture, true));
		}

		[Theory]
		[InlineData("texture", 1)]
		[InlineData("flat", 2)]
		[InlineData("sprite", 3)]
		[InlineData("", 0)]
		public void TargetsAndBrightmapFlags(string target, int expected)
		{
			var parser = Parse("BrIgHtMaP " + target + " TEST { map \"brightmaps/test.png\" disablefullbright iwad thiswad }", out bool result);
			Assert.True(result, parser.ErrorDescription);
			var material = parser.Materials.Single();
			Assert.Equal(expected, (int)material.Target);
			Assert.Equal("brightmaps/test.png", material.Brightmap);
			Assert.True(material.DisableFullbright);
			Assert.True(material.Overridable);
		}

		[Fact]
		public void HardwarePropertiesAndPostprocess()
		{
			var parser = Parse("HardwareShader PostProcess scene { shader bad { nested ignored } }\n" +
				"HardwareShader TEST { shader \"shaders/test.fp\" speed -2.5 material pbrbrightmap nomipmap disablealphatest define FIRST define SECOND=2 texture detail \"textures/detail.png\" }", out bool result);
			Assert.True(result, parser.ErrorDescription);
			var material = parser.Materials.Single();
			Assert.Equal(MaterialTarget.Any, material.Target);
			Assert.False(material.Overridable);
			Assert.Equal(-2.5f, material.Speed);
			Assert.Equal(MaterialModel.Pbr, material.ShaderModel);
			Assert.Equal(new[] { "FIRST", "SECOND 2" }, material.Defines.ToArray());
			Assert.Equal("detail", material.Textures.Single().Key);
			Assert.True(material.DisableAlphaTest);
		}

		[Fact]
		public void MaterialLayersAndMergingKeepIndependentProperties()
		{
			var parser = Parse("material flat TEST { normal n specular s metallic m roughness r ao a brightmap b glossiness 32 specularlevel 0.5 shader old }\n" +
				"brightmap TEST { map newer }\nhardwareshader TEST { shader new speed 2 }", out bool result);
			Assert.True(result, parser.ErrorDescription);
			var merged = new GldefsMaterial();
			foreach(var material in parser.Materials) merged.Merge(material);
			Assert.Equal(6, merged.Layers.Count);
			Assert.Equal(32f, merged.Glossiness);
			Assert.Equal(0.5f, merged.SpecularLevel);
			Assert.Equal("newer", merged.Brightmap);
			Assert.Equal("new", merged.Shader);
			Assert.Equal(2f, merged.Speed);
			Assert.Equal(MaterialModel.Default, merged.ShaderModel);
		}

		[Fact]
		public void LayerFloatsDefaultWhenAbsentAndMergeKeepsEarlierValues()
		{
			var parser = Parse("material texture TEST { glossiness 1e1 }\nmaterial texture TEST { specularlevel .25 }\nmaterial texture TEST { normal n }", out bool result);
			Assert.True(result, parser.ErrorDescription);
			Assert.Null(parser.Materials[2].Glossiness);
			Assert.Null(parser.Materials[2].SpecularLevel);
			var merged = new GldefsMaterial();
			foreach(var material in parser.Materials) merged.Merge(material);
			Assert.Equal(10f, merged.Glossiness);
			Assert.Equal(0.25f, merged.SpecularLevel);
			Assert.Equal(10f, GldefsMaterial.DefaultGlossiness);
			Assert.Equal(0.1f, GldefsMaterial.DefaultSpecularLevel);
		}

		// UZDoom's FMaterial rule over every layer combination: Specular first, PBR needs all four, normal is required
		[Fact]
		public void ModelSelectionTruthTable()
		{
			var failures = new List<string>();
			for(int mask = 0; mask < 32; mask++)
			{
				bool n = (mask & 1) != 0, s = (mask & 2) != 0, m = (mask & 4) != 0, r = (mask & 8) != 0, a = (mask & 16) != 0;
				MaterialModel expected = !n ? MaterialModel.Default : s ? MaterialModel.Specular : m && r && a ? MaterialModel.Pbr : MaterialModel.Default;
				if(MaterialModels.Select(n, s, m, r, a, UserShaderDialect.UZDoom) != expected) failures.Add("uzdoom " + mask);
				if(MaterialModels.Select(n, s, m, r, a, UserShaderDialect.Zandronum) != MaterialModel.Default) failures.Add("zandronum " + mask);
				if(MaterialModels.Select(n, s, m, r, a, UserShaderDialect.None) != MaterialModel.Default) failures.Add("none " + mask);
			}
			Assert.Empty(failures);
		}

		[Theory]
		[InlineData("", 0)]
		[InlineData("material normal", 0)]
		[InlineData("material brightmap", 0)]
		[InlineData("material specular", 1)]
		[InlineData("material specularbrightmap", 1)]
		[InlineData("material pbr", 2)]
		[InlineData("material PBRBrightmap", 2)]
		public void HardwareShaderTypeMapsToModel(string keyword, int expected)
		{
			var parser = Parse("hardwareshader TEST { shader s " + keyword + " }", out bool result);
			Assert.True(result, parser.ErrorDescription);
			Assert.Equal((MaterialModel)expected, parser.Materials.Single().ShaderModel);
		}

		[Fact]
		public void MaterialBlockShaderIsTypedFromLayersAndRejectsTheMaterialKeyword()
		{
			var logger = typeof(CodeImp.DoomBuilder.General).GetField("errorlogger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
			object previous = logger.GetValue(null);
			var errors = new CodeImp.DoomBuilder.ErrorLogger();
			logger.SetValue(null, errors);
			try
			{
				var parser = Parse("material texture TEST { shader s material pbr\nnormal n }\nhardwareshader NEXT { shader t }", out bool result);
				Assert.True(result, parser.ErrorDescription);
				Assert.Null(parser.Materials[0].ShaderModel);
				Assert.Equal("n", parser.Materials[0].Layers["normal"]);
				Assert.Equal(MaterialModel.Default, parser.Materials[1].ShaderModel);
				Assert.True(errors.GetErrors(0).Any(item => item.Description.Contains("Unknown keyword \"material\"")));
			}
			finally { logger.SetValue(null, previous); }
		}

		[Fact]
		public void ShaderAppliesOnlyToItsModelUnderUZDoom()
		{
			var typed = new GldefsMaterial { Shader = "s", ShaderModel = MaterialModel.Specular };
			Assert.True(MaterialModels.ShaderApplies(typed, MaterialModel.Specular, UserShaderDialect.UZDoom));
			Assert.False(MaterialModels.ShaderApplies(typed, MaterialModel.Default, UserShaderDialect.UZDoom));
			Assert.False(MaterialModels.ShaderApplies(new GldefsMaterial { Shader = "s", ShaderModel = MaterialModel.Default }, MaterialModel.Pbr, UserShaderDialect.UZDoom));
			Assert.True(MaterialModels.ShaderApplies(typed, MaterialModel.Default, UserShaderDialect.Zandronum));
			Assert.True(MaterialModels.ShaderApplies(new GldefsMaterial { Shader = "s" }, MaterialModel.Pbr, UserShaderDialect.UZDoom));
		}

		[Fact]
		public void AutoLayersFillOnlyAbsentLayers()
		{
			var files = new HashSet<string> { "brightmaps/auto/WALL", "materials/brightmaps/auto/WALL", "materials/normalmaps/auto/WALL",
				"materials/specular/auto/WALL", "materials/metallic/auto/WALL", "materials/roughness/auto/WALL", "materials/ao/auto/WALL" };
			Func<string, string> find = name => files.Contains(name) ? name + ".png" : null;
			Assert.Null(GldefsMaterial.AddAutoLayers(null, "OTHER", find));
			var auto = GldefsMaterial.AddAutoLayers(null, "WALL", find)!;
			Assert.Equal("brightmaps/auto/WALL.png", auto.Brightmap);
			foreach(var pair in new[] { ("normal", "normalmaps"), ("specular", "specular"), ("metallic", "metallic"), ("roughness", "roughness"), ("ao", "ao") })
				Assert.Equal("materials/" + pair.Item2 + "/auto/WALL.png", auto.Layers[pair.Item1]);
			var explicitlayers = new GldefsMaterial { Brightmap = "mine" };
			explicitlayers.Layers["normal"] = "mynormal";
			Assert.Same(explicitlayers, GldefsMaterial.AddAutoLayers(explicitlayers, "WALL", find));
			Assert.Equal("mine", explicitlayers.Brightmap);
			Assert.Equal("mynormal", explicitlayers.Layers["normal"]);
			Assert.Equal("materials/ao/auto/WALL.png", explicitlayers.Layers["ao"]);
		}

		[Fact]
		public void UnknownPropertyDoesNotSwallowLightOrFollowingBlock()
		{
			var parser = Parse("brightmap TEST { unknown ignored\n map bright }\npointlight test { color 1 1 1 size 32 }\nhardwareshader OTHER { shader next }", out bool result);
			Assert.True(result, parser.ErrorDescription);
			Assert.Equal(2, parser.Materials.Count);
			Assert.Equal(1, parser.LightsByName.Count);
		}

		[Theory]
		[InlineData("brightmap TEST { map")]
		[InlineData("material TEST { shader x")]
		[InlineData("hardwareshader TEST { speed nope }")]
		[InlineData("hardwareshader TEST { material unknown }")]
		[InlineData("material TEST { glossiness shiny }")]
		[InlineData("material TEST { specularlevel NaN }")]
		public void MalformedBlocksReportErrors(string text)
		{
			var parser = Parse(text, out bool result);
			Assert.False(result);
			Assert.True(parser.HasError);
		}
	}
}
