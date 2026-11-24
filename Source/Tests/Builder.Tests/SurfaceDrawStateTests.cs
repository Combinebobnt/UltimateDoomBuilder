using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	public class SurfaceDrawStateTests
	{
		private delegate void Mutate(ref SurfaceDrawState s);

		private static SurfaceDrawState Base()
		{
			return new SurfaceDrawState {
				Shader = ShaderName.world3d_main, Fog = false, SectorLightLevel = 160, FogFactor = 0.25f,
				LightParams = new Vector4f(8f, -1f, 0f, 0f), SendFogColor = true, SectorFogColor = new Color4(0.1f, 0.2f, 0.3f, 1f),
				Highlight = new Color4(0f, 0f, 0f, 0f), Desaturation = 0f, Skew = new Vector2f(0f, 0f),
				GlowTopColor = new Vector4f(), GlowBottomColor = new Vector4f(), GlowTopPlane = new Vector4f(), GlowBottomPlane = new Vector4f() };
		}

		// A field left out of SameAs would let a merged draw use the previous surface's value
		[Fact]
		public void EveryFieldTakesPartInEquality()
		{
			var mutations = new Dictionary<string, Mutate> {
				{ "Shader", (ref SurfaceDrawState s) => s.Shader = ShaderName.world3d_main_highlight },
				{ "Fog", (ref SurfaceDrawState s) => s.Fog = true },
				{ "SectorLightLevel", (ref SurfaceDrawState s) => s.SectorLightLevel = 161 },
				{ "FogFactor", (ref SurfaceDrawState s) => s.FogFactor = 0.5f },
				{ "LightParams", (ref SurfaceDrawState s) => s.LightParams.Y = 0.5f },
				{ "SendFogColor", (ref SurfaceDrawState s) => s.SendFogColor = false },
				{ "SectorFogColor", (ref SurfaceDrawState s) => s.SectorFogColor.Blue = 0.9f },
				{ "Highlight", (ref SurfaceDrawState s) => s.Highlight.Alpha = 0.5f },
				{ "Desaturation", (ref SurfaceDrawState s) => s.Desaturation = 0.5f },
				{ "Skew", (ref SurfaceDrawState s) => s.Skew.Y = 1f },
				{ "GlowTopColor", (ref SurfaceDrawState s) => s.GlowTopColor.X = 1f },
				{ "GlowBottomColor", (ref SurfaceDrawState s) => s.GlowBottomColor.W = 1f },
				{ "GlowTopPlane", (ref SurfaceDrawState s) => s.GlowTopPlane.Z = 1f },
				{ "GlowBottomPlane", (ref SurfaceDrawState s) => s.GlowBottomPlane.Y = 1f },
				// Bit-exact like the native memcmp, so a sign flip on zero is a change
				{ "negative zero", (ref SurfaceDrawState s) => s.Desaturation = -0f },
			};
			SurfaceDrawState a = Base();
			List<string> missed = new List<string>();
			foreach(KeyValuePair<string, Mutate> m in mutations)
			{
				SurfaceDrawState b = Base();
				m.Value(ref b);
				if(a.SameAs(ref b) || b.SameAs(ref a)) missed.Add(m.Key);
			}
			Assert.True(missed.Count == 0, "SameAs ignores: " + string.Join(", ", missed));
			SurfaceDrawState c = Base();
			Assert.True(a.SameAs(ref c));
		}

		[Fact]
		public void FogColorCountsOnlyWhenSent()
		{
			SurfaceDrawState a = Base(), b = Base();
			a.SendFogColor = b.SendFogColor = false;
			b.SectorFogColor = new Color4(1f, 1f, 1f, 1f);
			Assert.True(a.SameAs(ref b));
		}
	}
}
