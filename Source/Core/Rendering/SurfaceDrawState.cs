namespace CodeImp.DoomBuilder.Rendering
{
	// The per-surface values the 3D geometry loop sends before each draw. Two surfaces with the same state
	// can share one draw call; for now the loop only counts how often consecutive surfaces differ.
	internal struct SurfaceDrawState
	{
		internal ShaderName Shader;
		internal bool Fog; // fogEnabled, which a material program reads instead of the +8 fog shaders
		internal int SectorLightLevel;
		internal float FogFactor; // campos.w; xyz is the camera, the same for every surface of a frame
		internal Vector4f LightParams;
		internal bool SendFogColor; // false: sectorfogcolor keeps whatever an earlier surface sent
		internal Color4 SectorFogColor;
		internal Color4 Highlight;
		internal float Desaturation;
		internal Vector2f Skew;
		internal Vector4f GlowTopColor, GlowBottomColor, GlowTopPlane, GlowBottomPlane;

		// Bit-exact, like the native SetUniform's memcmp; the fog colour counts only when it is sent
		internal bool SameAs(ref SurfaceDrawState o)
		{
			return Shader == o.Shader && Fog == o.Fog && SectorLightLevel == o.SectorLightLevel && Same(FogFactor, o.FogFactor)
				&& Same(LightParams, o.LightParams) && SendFogColor == o.SendFogColor
				&& (!SendFogColor || Same(SectorFogColor, o.SectorFogColor)) && Same(Highlight, o.Highlight)
				&& Same(Desaturation, o.Desaturation) && Same(Skew.X, o.Skew.X) && Same(Skew.Y, o.Skew.Y)
				&& Same(GlowTopColor, o.GlowTopColor) && Same(GlowBottomColor, o.GlowBottomColor)
				&& Same(GlowTopPlane, o.GlowTopPlane) && Same(GlowBottomPlane, o.GlowBottomPlane);
		}

		private static unsafe bool Same(float a, float b) { return *(int*)&a == *(int*)&b; }
		private static bool Same(Vector4f a, Vector4f b) { return Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z) && Same(a.W, b.W); }
		private static bool Same(Color4 a, Color4 b) { return Same(a.Red, b.Red) && Same(a.Green, b.Green) && Same(a.Blue, b.Blue) && Same(a.Alpha, b.Alpha); }
	}
}
