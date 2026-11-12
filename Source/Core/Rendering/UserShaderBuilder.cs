using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.Rendering.Shaders;

namespace CodeImp.DoomBuilder.Rendering
{
	internal enum UserShaderDialect { None, Zandronum, UZDoom }

	internal sealed class UserShaderProgram
	{
		internal ShaderName Index;
		internal string Path;
		internal string VertexSource;
		internal string FragmentSource;
		internal bool Compiled;
		internal bool Usable;
		internal bool IsWarp;
		private float timer;

		internal float GetTimer(UserShaderDialect dialect, float speed, double seconds)
		{
			if(dialect != UserShaderDialect.Zandronum || speed > 0.0f) timer = (float)(seconds * speed);
			return timer;
		}
	}

	internal static class UserShaderBuilder
	{
		internal static UserShaderDialect Dialect(string engine)
		{
			if(string.Equals(engine, "zandronum", StringComparison.OrdinalIgnoreCase)) return UserShaderDialect.Zandronum;
			return GldefsMaterial.IsGZDoom(engine) ? UserShaderDialect.UZDoom : UserShaderDialect.None;
		}

		internal static string Key(GldefsMaterial material, UserShaderDialect dialect)
		{
			var key = new StringBuilder(material.Shader.Replace('\\', '/').ToLowerInvariant() + "\n" + dialect + "\n");
			foreach(string define in material.Defines) key.Append("#define ").Append(define).Append('\n');
			for(int i = 0; i < material.Textures.Count; i++) key.Append("#define ").Append(material.Textures[i].Key).Append(" texture").Append(i + 4).Append('\n');
			return key.ToString();
		}

		internal static string CleanSource(string source)
		{
			// Keep line breaks so diagnostics after removed legacy uniforms still name the user line.
			source = Regex.Replace(source, @"\buniform\s+(?:float\s+timer|sampler2D\s+tex)\s*;", match => Regex.Replace(match.Value, @"[^\r\n]", " "));
			return source.Replace("gl_TexCoord[0]", "vTexCoord");
		}

		private static string Resource(string name)
		{
			using(var stream = typeof(UserShaderBuilder).Assembly.GetManifestResourceStream("CodeImp.DoomBuilder.Resources." + name))
			using(var reader = new StreamReader(stream)) return reader.ReadToEnd();
		}

		internal static UserShaderDialect WarpDialect(string engine)
		{
			return string.Equals(engine, "zandronum", StringComparison.OrdinalIgnoreCase) ? UserShaderDialect.Zandronum : UserShaderDialect.UZDoom;
		}

		internal static string WarpKey(int style, UserShaderDialect dialect)
		{
			return "builtin:warp" + style + ":" + dialect;
		}

		internal static UserShaderProgram BuildWarp(int style, UserShaderDialect dialect)
		{
			if(style != 1 && style != 2) throw new ArgumentOutOfRangeException("style");
			string source = Resource("warp" + style + "_" + (dialect == UserShaderDialect.Zandronum ? "zandronum" : "uzdoom") + ".glsl");
			var program = Build(new GldefsMaterial { Shader = WarpKey(style, dialect) }, dialect, source);
			program.IsWarp = true;
			return program;
		}

		internal static UserShaderProgram Build(GldefsMaterial material, UserShaderDialect dialect, string source)
		{
			var shader = ShaderCompiler.Compile(Resource("world3d.shader")).GetShader("world3d_main_fog");
			string shared = shader.GetSharedSource();
			string vertex = @"
layout(location=0) in vec3 AttrPosition;
layout(location=1) in vec4 AttrColor;
layout(location=2) in vec2 AttrUV;
layout(location=3) in vec3 AttrNormal;
uniform mat4 world, view, projection, modelnormal;
uniform vec2 skew;
out vec4 pixelpos, vTexCoord, viewpos, Color;
out vec3 PosW, Normal;
void main()
{
    vec4 pos = world * vec4(AttrPosition, 1.0);
    viewpos = view * pos;
    gl_Position = projection * viewpos;
    PosW = pos.xyz;
    Normal = normalize((modelnormal * vec4(AttrNormal, 1.0)).xyz);
    Color = AttrColor;
    pixelpos = vec4(pos.xzy, -viewpos.z / viewpos.w);
    vTexCoord = vec4(AttrUV + vec2(0.0, (AttrUV.x - skew.x) * skew.y), 0.0, 1.0);
}
";
			var fragment = new StringBuilder(shared);
			fragment.Append(Resource("usershader_prelude.glsl"));
			if(dialect == UserShaderDialect.Zandronum) fragment.AppendLine("#define DOOMLIGHTFACTOR 232.0");
			foreach(string define in material.Defines) fragment.Append("#define ").AppendLine(define);
			for(int i = 0; i < material.Textures.Count; i++) fragment.Append("#define ").Append(material.Textures[i].Key).Append(" texture").AppendLine((i + 4).ToString());
			bool old = !source.Contains("SetupMaterial") && !source.Contains("ProcessMaterial");
			bool legacy = source.Contains("ProcessMaterial") && !source.Contains("SetupMaterial");
			if(dialect == UserShaderDialect.UZDoom && legacy) fragment.AppendLine("#define LEGACY_USER_SHADER");
			fragment.AppendLine("#line 1 1").AppendLine(CleanSource(source)).AppendLine("#line 1 2");
			if(dialect == UserShaderDialect.UZDoom)
			{
				if(old)
				{
					if(source.Contains("GetTexCoord")) fragment.AppendLine("void SetupMaterial(inout Material material) { SetMaterialProps(material, GetTexCoord()); }");
					else fragment.AppendLine("void SetupMaterial(inout Material material) { material.Base = " + (source.Contains("ProcessTexel") ? "ProcessTexel()" : "Process(vec4(1.0))") + "; material.Normal = ApplyNormalMap(vTexCoord.st); material.Bright = editorBright(vTexCoord.st); }");
					if(source.Contains("ProcessLight")) fragment.AppendLine("vec4 ProcessLight(Material material, vec4 color) { return ProcessLight(color); }");
				}
				if(!source.Contains("ProcessLight")) fragment.AppendLine("vec4 ProcessLight(Material material, vec4 color) { return color; }");
			}
			fragment.AppendLine("void main() {");
			fragment.AppendLine("vec4 baseLight = useVertexColorUniform > 0.0 ? vertexColor : Color;");
			if(dialect == UserShaderDialect.Zandronum)
			{
				fragment.AppendLine("vec3 additive = getDynLightContribution(vec4(0.0), vec4(0.0), PosW, Normal).rgb;");
				fragment.AppendLine("baseLight.rgb = min(baseLight.rgb + editorBright(vTexCoord.st).rgb, vec3(1.0));");
				fragment.AppendLine("vec4 light = getDynLightContribution(vec4(1.0), baseLight, PosW, Normal); light.rgb -= additive;");
				fragment.AppendLine("vec4 frag = Process(light); frag.rgb += additive;");
			}
			else
			{
				fragment.AppendLine("Material material; material.Base = vec4(0.0); material.Bright = vec4(0.0); material.Glow = vec4(0.0); material.Normal = vec3(0.0); material.Specular = vec3(0.0); material.Glossiness = 0.0; material.SpecularLevel = 0.0; material.Metallic = 0.0; material.Roughness = 0.0; material.AO = 0.0;");
				fragment.AppendLine("#ifdef LEGACY_USER_SHADER\nmaterial = ProcessMaterial();\n#else\nSetupMaterial(material);\n#endif");
				fragment.AppendLine("baseLight.rgb = min(baseLight.rgb + desaturate(material.Bright).rgb, vec3(1.0));");
				fragment.AppendLine("vec4 frag = getDynLightContribution(material.Base, ProcessLight(material, baseLight), PosW, Normal); frag.rgb += material.Glow.rgb;");
			}
			fragment.AppendLine(@"
frag = mix(frag, vec4(stencilColor.rgb, frag.a), stencilColor.a);
if(fogEnabled > 0.0) frag = getFogColor(PosW, frag);
frag = desaturate(frag);
if(highlightcolor.a > 0.0 && frag.a != 0.0) frag = vec4(highlightcolor.rgb * highlightcolor.a + frag.rgb - 0.4 * highlightcolor.a, max(frag.a + 0.25, 0.5));
#if defined(ALPHA_TEST)
if(disableAlphaTest < 0.5 && frag.a < 0.5) discard;
#endif
if(fogsettings.x >= 0.0) frag = mix(frag, fogcolor, clamp((-viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
FragColor = frag;
}");
			return new UserShaderProgram { Path = material.Shader, VertexSource = vertex, FragmentSource = fragment.ToString() };
		}
	}
}
