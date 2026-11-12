using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.GZBuilder.Data;
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
			Assert.Equal("pbrbrightmap", material.MaterialType);
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
			Assert.Equal(8, merged.Layers.Count);
			Assert.Equal("32", merged.Layers["glossiness"]);
			Assert.Equal("newer", merged.Brightmap);
			Assert.Equal("new", merged.Shader);
			Assert.Equal(2f, merged.Speed);
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
		public void MalformedBlocksReportErrors(string text)
		{
			var parser = Parse(text, out bool result);
			Assert.False(result);
			Assert.True(parser.HasError);
		}
	}
}
