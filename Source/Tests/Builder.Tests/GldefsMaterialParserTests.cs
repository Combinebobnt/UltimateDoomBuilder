using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.ZDoom;
using System.Text;

namespace Builder.Tests
{
	public class GldefsMaterialParserTests
	{
		private static GldefsParser Parse(string text, out bool result)
		{
			var data = new TextResourceData(new MemoryStream(Encoding.UTF8.GetBytes(text)),
				new DataLocation(DataLocation.RESOURCE_DIRECTORY, string.Empty, false, false, false, new List<string>()), "GLDEFS");
			var parser = new GldefsParser();
			result = parser.Parse(data, true);
			return parser;
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
