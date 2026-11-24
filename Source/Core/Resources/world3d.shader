uniforms
{
	mat4 world;
	mat4 view;
	mat4 projection;
	mat4 modelnormal;
	vec4 campos;
	
	vec4 highlightcolor;
	vec4 stencilColor;
	float desaturation;

	// Wall glow: colour with height in alpha, and the ceiling and floor planes (a, b, c, d) it fades from
	vec4 glowTopColor;
	vec4 glowBottomColor;
	vec4 glowTopPlane;
	vec4 glowBottomPlane;

	vec4 fogsettings;
	vec4 fogcolor;
	vec4 sectorfogcolor;
	vec4 vertexColor;

	// Light model: x kind (0 Editor, 1 ZDoomSoftware, 2 Hardware), y Editor doom curve, z hardware mode, w distance on
	vec4 lightmodel;
	// The vertex light of _vertexcolor programs, in the Light attribute's encoding
	vec4 lightoverride;
	// Added to the sector light (dynamic light on things)
	vec4 vertexColorAdd;
	// x software globVis, y black fog density (< 0 none, 0 from the fog light level, > 0 this density)
	vec4 lightparams;

	sampler2D texture1;
	sampler2D texture2;
	sampler2D texture3;
	float brightmapEnabled;

	// classic lighting related
	int drawPaletted;
	ivec2 colormapSize;
	int doomlightlevels;
	int sectorLightLevel;

	// dynamic light related
	vec4 lightPosAndRadius[64];
	vec4 lightOrientation[64]; // this is a vector that points in light's direction
	vec2 light2Radius[64]; // this is used with spotlights
	vec2 lightStrengthAndLinearity[64]; // this is used with vkdoom lights
	float useLightStrength;
	vec4 lightColor[64];
	float ignoreNormals;
	float lightsEnabled;

	// Slope handle length
	float slopeHandleLength;

	// Skewing
	vec2 skew; // the x component is used to negate the texture offset for the skew computation. There must be a better way

}

functions
{
	float inverseSquareDistanceAttenuation(float dist, float radius, float strength, float linearity)
	{
		float a = dist / radius;
		float b = clamp(1.0 - a * a * a * a, 0.0, 1.0);
		return mix((b * b) / (dist * dist + 1.0) * strength, clamp((radius - dist) / radius, 0.0, 1.0), linearity);
	}
	
    vec4 getColorMappedColor(int entry, int depth)
    {
        vec2 uv = vec2((float(entry) + 0.5) / colormapSize.x, (float(depth) + 0.5) / colormapSize.y);
        vec4 colormapColor = texture(texture2, uv);
        return colormapColor;
    }
    
	// The level a Light attribute carries after fake contrast, before any curve
	float attrLightLevel(vec4 light)
	{
		return round(light.g * 255.0);
	}

	vec4 desaturate(vec4 texel)
	{
		float gray = (texel.r * 0.3 + texel.g * 0.56 + texel.b * 0.14);	
		return vec4(mix(texel.rgb, vec3(gray), desaturation), texel.a);
	}

	// The game models desaturate the texel and what is added to the light (glow, dynamic light), never the light
	// colour or the fog (main.fp getLightColor); the Editor model desaturates the finished fragment
	vec4 desaturatePart(vec4 c)
	{
		return lightmodel.x > 0.0 ? desaturate(c) : c;
	}

	vec4 desaturateFinal(vec4 c)
	{
		return lightmodel.x > 0.0 ? c : desaturate(c);
	}

	// C integer division by 5 (truncation toward zero) of an integral value, safe against an inexact divide
	float truncDiv5(float x)
	{
		return trunc((x + sign(x) * 0.5) / 5.0);
	}

	// UZDoom's CalcLightLevel for hardware light modes 0-4; modes 2 and 3 use the dark curve
	float hardwareLightLevel(float level, float rel)
	{
		if (level <= 0.0) return 0.0;
		float light = level + rel;
		if ((lightmodel.z == 2.0 || lightmodel.z == 3.0) && level < 192.0)
		{
			if (level > 100.0)
			{
				light = roundEven(192.0 - (192.0 - level) * 1.87);
				light = light + rel < 20.0 ? 20.0 + truncDiv5(light + rel - 20.0) : light + rel;
			}
			else light = truncDiv5(level + rel);
		}
		return clamp(light, 1.0, 255.0);
	}

	// UZDoom's distfogtable at gl_distfog 70: [0] for light mode 0, [1] for the others
	float distFogDensity(float foglevel)
	{
		int i = int(clamp(foglevel, 0.0, 255.0));
		if (lightmodel.z == 0.0)
		{
			if (i < 164) return float(35 + (70 * (164 - i)) / 164);
			if (i < 230) return float(35 - (35 * (i - 164)) / 66);
			return 0.0;
		}
		if (i < 128) return 6.0 + float(35 + (70 * (128 - i)) / 48);
		if (i < 216) return (216.0 - float(i)) / 88.0 * 70.0 / 10.0;
		return 0.0;
	}

	// Light mode 2's brightening near the camera (SetShaderLight, main.fp getLightColor)
	float doomNearBrightening(float level, float olight, float fogdist)
	{
		float lightdist = 256.0;
		if (olight < 96.0)
		{
			lightdist = 128.0 + olight * 256.0 / 96.0 / 2.0;
			olight = 96.0;
		}
		float factor = 1.0 + (olight / level - 1.0) * 0.75;
		if (factor == 1.0 || fogdist >= lightdist) return 1.0;
		return factor - (fogdist / lightdist) * (factor - 1.0);
	}

	// Sector light on a light colour by the map's light model; light.a == 0 means the colour is final
	vec4 sectorLight(vec4 color, vec4 light, vec3 PosW)
	{
		if (light.a < 0.5) return color + desaturatePart(vertexColorAdd);
		float level = attrLightLevel(light);
		bool distanceOn = lightmodel.w > 0.0;

		// ZDoomSoftware: 1 - R_DoomLightingEquation, z the radial distance (gl_fogmode 2)
		if (lightmodel.x == 1.0 && distanceOn)
		{
			float vis = min(lightparams.x / distance(PosW, campos.xyz), 24.0 / 32.0);
			float colormap = (2.0 - (level + 12.0) / 128.0 - vis) * 31.0;
			return vec4(color.rgb * (1.0 - clamp(colormap, 0.0, 31.0) / 32.0), color.a) + desaturatePart(vertexColorAdd);
		}

		// Hardware: CalcLightColor's integer scaling, then black distance fog in the light only
		if (lightmodel.x == 2.0)
		{
			float base = round(light.r * 255.0);
			float hwlevel = hardwareLightLevel(base, level - base);
			vec3 rgb = floor(round(color.rgb * 255.0) * hwlevel / 255.0 + 0.001) / 255.0;
			if (distanceOn && lightparams.y >= 0.0)
			{
				float density = lightparams.y > 0.0 ? lightparams.y : distFogDensity(round(light.b * 255.0));
				if (density > 0.0)
				{
					float fogdist = max(16.0, distance(PosW, campos.xyz));
					if (lightmodel.z == 2.0 && hwlevel > 0.0) rgb *= doomNearBrightening(hwlevel, base, fogdist);
					rgb *= exp2(density * (-1.442692 / 64000.0) * fogdist);
				}
			}
			return vec4(rgb, color.a) + desaturatePart(vertexColorAdd);
		}

		// Editor curve (also ZDoomSoftware with distance effects off)
		if (lightmodel.y > 0.0 && level < 192.0) level = max(trunc(192.0 - (192.0 - level) * 1.5), 0.0);
		return vec4(color.rgb * (level / 255.0), color.a) + desaturatePart(vertexColorAdd);
	}

    int lightLevelFromVertexColor(vec3 color)
    {
        float result = max(max(color.r, color.g), color.b) * 255;
        if (result < 192 && doomlightlevels > 0) {
            // correct for darkening in Doom light mode
            result = -0.666667 * (-96 - result);
        }
        return int(result);
    }

	int classicLightLevel(vec3 color, vec4 light)
	{
		return light.a < 0.5 ? lightLevelFromVertexColor(color) : int(attrLightLevel(light));
	}
        
    int classicLightLevelToColorMapOffset(int lightLevel, vec3 position, vec3 normal, bool hvmod)
    {
        const int LIGHTLEVELS = 16;
        const int LIGHTSEGSHIFT = 4;
        const int NUMCOLORMAPS = 32;
        const int MAXLIGHTSCALE = 48;
        const int DISTMAP = 2;
        
        int scaledLightLevel = lightLevel >> LIGHTSEGSHIFT;
        
        bool isFlat = abs(dot(normal, vec3(0, 0, 1))) > 1e-3; 
        
        if (hvmod) {
            if (abs(dot(normal, vec3(0, 1, 0))) < 1e-3)
            {
                scaledLightLevel++;
            }
            else if (abs(dot(normal, vec3(1, 0, 0))) < 1e-3)
            {
                scaledLightLevel--;
            }
        }
        
        int level;
        float dist = distance(position, campos.xyz);
        
        if (!isFlat) 
        {
            int startmap = int(((LIGHTLEVELS-1-scaledLightLevel)*2)*NUMCOLORMAPS/LIGHTLEVELS);
            
            // same calculation as Eternity Engine
            int index = int(2560.0 / dist);
            if (index >= MAXLIGHTSCALE) index = MAXLIGHTSCALE - 1;
            level = startmap - index / DISTMAP;
        }
        else
        {
            // same calculation as Eternity Engine
            float startmap = 2.0 * (30.0 - lightLevel / 8.0f);
            level = int(startmap - (1280.0f / dist)) + 1;
        }
        
       
        if (level < 0) level = 0;
        if (level >= NUMCOLORMAPS) level = NUMCOLORMAPS - 1;
        return level;
    }

	// This adds fog color to current pixel color
	vec4 getFogColor(vec3 PosW, vec4 color)
	{
		float fogdist = max(16.0, distance(PosW, campos.xyz));
		float fogfactor = exp2(campos.w * fogdist);

		color.rgb = mix(sectorfogcolor.rgb, color.rgb, fogfactor);
		return color;
	}

	vec3 getOneDynLightContribution(vec3 PosW, vec3 Normal, vec3 light, vec4 lColor, vec4 lPosAndRadius, vec4 lOrientation, vec2 l2Radius, vec2 lStrengthAndLinearity)
	{

		//is face facing away from light source?
		//      update 01.02.2017: offset the equation by 3px back to try to emulate GZDoom's broken visibility check.
		float diffuseContribution = dot(Normal, normalize(lPosAndRadius.xyz - PosW + Normal * 3.0));
		if (diffuseContribution < 0.0 && (ignoreNormals == 0 || (lColor.a > 0.979 && lColor.a < 0.981))) // attenuated light and facing away
			return light;
		
		diffuseContribution = max(diffuseContribution, 0.0); // to make sure

		//is pixel in light range?
		float dist = distance(PosW, lPosAndRadius.xyz);

		if (dist > lPosAndRadius.w)
			return light;

		float power = 1.0;

		if(useLightStrength > 0.0)
		{
			power *= inverseSquareDistanceAttenuation(dist, lPosAndRadius.w, lStrengthAndLinearity.x, lStrengthAndLinearity.y);
		}
		else
		{
			power *= max(lPosAndRadius.w - dist, 0.0) / lPosAndRadius.w;
		}

		if (lOrientation.w > 0.5)
		{
			vec3 lightDirection = normalize(lPosAndRadius.xyz - PosW);
			float cosDir = dot(lightDirection, lOrientation.xyz);
			float df = smoothstep(l2Radius.y, l2Radius.x, cosDir);
			power *= df;
		}

		if (lColor.a > 0.979 && lColor.a < 0.981) // attenuated light 98%
			power *= diffuseContribution;

		// for w/e reason GZDoom also does this
		power *= lColor.a;

		if (lColor.a >= 1)
			return light.rgb - lColor.rgb * power;

		return light.rgb + lColor.rgb * power;

	}

	vec4 getDynLightContribution(vec4 tcolor, vec4 baselight, vec3 PosW, vec3 Normal)
	{
		vec3 light = vec3(0, 0, 0);
		vec3 addlight = vec3(0, 0, 0);

		if (lightsEnabled != 0)
		{
			for (int i = 0; i < 64; i++)
			{
				if (lightColor[i].a == 0)
					break;
				if (lightColor[i].a < 0.4) // additive
					addlight = getOneDynLightContribution(PosW, Normal, addlight, lightColor[i], lightPosAndRadius[i], lightOrientation[i], light2Radius[i], lightStrengthAndLinearity[i]);
				else light = getOneDynLightContribution(PosW, Normal, light, lightColor[i], lightPosAndRadius[i], lightOrientation[i], light2Radius[i], lightStrengthAndLinearity[i]);
			}
		}

		return vec4(tcolor.rgb*(baselight.rgb+desaturatePart(vec4(light, 1.0)).rgb)+desaturatePart(vec4(addlight, 1.0)).rgb, tcolor.a*baselight.a);
	}

	// Glow from the ceiling and floor flats, fading over its height (the engine's main.fp arithmetic)
	vec3 wallGlow(vec3 PosW)
	{
		vec3 glow = vec3(0.0);
		if (glowTopColor.a > 0.0)
		{
			float dist = -(glowTopPlane.w + glowTopPlane.x * PosW.x + glowTopPlane.y * PosW.y) / glowTopPlane.z - PosW.z;
			if (dist < glowTopColor.a) glow += glowTopColor.rgb * (1.0 - dist / glowTopColor.a);
		}
		if (glowBottomColor.a > 0.0)
		{
			float dist = PosW.z + (glowBottomPlane.w + glowBottomPlane.x * PosW.x + glowBottomPlane.y * PosW.y) / glowBottomPlane.z;
			if (dist < glowBottomColor.a) glow += glowBottomColor.rgb * (1.0 - dist / glowBottomColor.a);
		}
		return glow;
	}

	// The glow is added after the sector light and before brightmaps and dynamic lights
	vec4 addWallGlow(vec4 light, vec3 PosW)
	{
		return vec4(min(light.rgb + desaturatePart(vec4(wallGlow(PosW), 1.0)).rgb, vec3(1.0)), light.a);
	}

	vec4 getMaterialLight(vec4 baselight, vec2 uv)
	{
		if(brightmapEnabled > 0.0)
			baselight.rgb = min(baselight.rgb + desaturate(texture(texture3, uv + vec2(0.0, (uv.x - skew.x) * skew.y))).rgb, vec3(1.0));
		return baselight;
	}
}

shader world3d_main
{
	in
	{
		vec3 Position;
		vec4 Color;
		vec2 TextureCoordinate;
		vec3 Normal;
		vec4 Light;
	}

	v2f
	{
		vec4 Color;
		vec2 UV;
		vec3 PosW;
		vec3 Normal;
		vec4 viewpos;
		vec3_flat flatNormal;
		vec4_flat flatColor;
		vec4 Light;
	}

	out
	{
		vec4 FragColor;
	}
	
	vertex
	{
		v2f.viewpos = view * world * vec4(in.Position, 1.0);
		gl_Position = projection * v2f.viewpos;
		v2f.PosW = (world * vec4(in.Position, 1.0)).xyz;
		v2f.Color = in.Color;
		v2f.UV = in.TextureCoordinate;
		v2f.Normal = normalize((modelnormal * vec4(in.Normal, 1.0)).xyz);
		v2f.flatColor = in.Color;
		v2f.flatNormal = normalize(in.Normal);
		v2f.Light = in.Light;
	}
	
	fragment
	{
		vec4 tcolor = desaturatePart(texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y)));
		tcolor = mix(tcolor, vec4(stencilColor.rgb, tcolor.a), stencilColor.a);
		tcolor = getDynLightContribution(tcolor, getMaterialLight(addWallGlow(sectorLight(v2f.Color, v2f.Light, v2f.PosW), v2f.PosW), v2f.UV), v2f.PosW, v2f.Normal);
		out.FragColor = desaturateFinal(tcolor);

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

shader world3d_fullbright extends world3d_main
{
	fragment
	{
		vec4 tcolor = texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y));
		tcolor = mix(tcolor, vec4(stencilColor.rgb, tcolor.a), stencilColor.a);
		tcolor.a *= v2f.Color.a;
		out.FragColor = tcolor;

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

shader world3d_main_highlight extends world3d_main
{
	fragment
	{
		vec4 tcolor = desaturatePart(texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y)));
		tcolor = mix(tcolor, vec4(stencilColor.rgb, tcolor.a), stencilColor.a);
		tcolor = getDynLightContribution(tcolor, getMaterialLight(addWallGlow(sectorLight(v2f.Color, v2f.Light, v2f.PosW), v2f.PosW), v2f.UV), v2f.PosW, v2f.Normal);
		if (tcolor.a == 0.0)
		{
			out.FragColor = tcolor;
		}
		else
		{
			// Blend texture color and vertex color
			vec4 ncolor = desaturateFinal(tcolor);

			out.FragColor = vec4(highlightcolor.rgb * highlightcolor.a + (ncolor.rgb - 0.4 * highlightcolor.a), max(v2f.Color.a + 0.25, 0.5));
		}

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

shader world3d_fullbright_highlight extends world3d_fullbright
{
	fragment
	{
		vec4 tcolor = texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y));
		tcolor = mix(tcolor, vec4(stencilColor.rgb, tcolor.a), stencilColor.a);
		if(tcolor.a == 0.0)
		{
			out.FragColor = tcolor;
		}
		else
		{
			// Blend texture color and vertex color
			vec4 ncolor = tcolor * v2f.Color;

			out.FragColor = vec4(highlightcolor.rgb * highlightcolor.a + (tcolor.rgb - 0.4 * highlightcolor.a), max(v2f.Color.a + 0.25, 0.5));
		}

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

shader world3d_vertex_color extends world3d_main
{
	fragment
	{
		out.FragColor = v2f.Color;

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

shader world3d_main_vertexcolor extends world3d_main
{
	vertex
	{
		v2f.viewpos = view * world * vec4(in.Position, 1.0);
		gl_Position = projection * v2f.viewpos;
		v2f.PosW = (world * vec4(in.Position, 1.0)).xyz;
		v2f.Color = vertexColor;
		v2f.UV = in.TextureCoordinate;
		v2f.Light = lightoverride;
	}
}

shader world3d_constant_color extends world3d_main_vertexcolor
{
	fragment
	{
		out.FragColor = vertexColor;

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0f) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

// world3d_main_highlight with vertex program from world3d_vertexcolor
// to-do: rewrite into a function
shader world3d_highlight_vertexcolor extends world3d_main_highlight
{
	vertex
	{
		v2f.viewpos = view * world * vec4(in.Position, 1.0);
		gl_Position = projection * v2f.viewpos;
		v2f.PosW = (world * vec4(in.Position, 1.0)).xyz;
		v2f.Color = vertexColor;
		v2f.UV = in.TextureCoordinate;
		v2f.Light = lightoverride;
	}
}

shader world3d_main_fog extends world3d_main
{
	fragment
	{
		vec4 tcolor = desaturatePart(texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y)));
		tcolor = mix(tcolor, vec4(stencilColor.rgb, tcolor.a), stencilColor.a);
		tcolor = getDynLightContribution(tcolor, getMaterialLight(addWallGlow(sectorLight(v2f.Color, v2f.Light, v2f.PosW), v2f.PosW), v2f.UV), v2f.PosW, v2f.Normal);
		if (tcolor.a == 0.0)
		{
			out.FragColor = tcolor;
		}
		else
		{
			out.FragColor = desaturateFinal(getFogColor(v2f.PosW, tcolor));
		}

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

shader world3d_main_highlight_fog extends world3d_main_fog
{
	fragment
	{
		vec4 tcolor = desaturatePart(texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y)));
		tcolor = mix(tcolor, vec4(stencilColor.rgb, tcolor.a), stencilColor.a);
		tcolor = vec4(getDynLightContribution(tcolor, getMaterialLight(addWallGlow(sectorLight(v2f.Color, v2f.Light, v2f.PosW), v2f.PosW), v2f.UV), v2f.PosW, v2f.Normal).rgb, tcolor.a);
		if (tcolor.a == 0.0)
		{
			out.FragColor = tcolor;
		}
		else
		{
			// Blend texture color and vertex color
			vec4 ncolor = desaturateFinal(getFogColor(v2f.PosW, tcolor));

			out.FragColor = vec4(highlightcolor.rgb * highlightcolor.a + (ncolor.rgb - 0.4 * highlightcolor.a), max(ncolor.a + 0.25, 0.5));
		}

		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif

		if (fogsettings.x >= 0.0) out.FragColor = mix(out.FragColor, fogcolor, clamp((-v2f.viewpos.z - fogsettings.x) / (fogsettings.y - fogsettings.x), 0.0, 1.0));
	}
}

// world3d_fog, but with vertex shader from customvertexcolor_fog
// to-do: rewrite into a function
shader world3d_main_fog_vertexcolor extends world3d_main_fog
{
	vertex
	{
		v2f.viewpos = view * world * vec4(in.Position, 1.0);
		gl_Position = projection * v2f.viewpos;
		v2f.PosW = (world * vec4(in.Position, 1.0)).xyz;
		v2f.Color = vertexColor;
		v2f.UV = in.TextureCoordinate;
		v2f.Normal = normalize((modelnormal * vec4(in.Normal, 1.0)).xyz);
		v2f.Light = lightoverride;
	}
}

shader world3d_main_highlight_fog_vertexcolor extends world3d_main_highlight_fog
{
	vertex
	{
		v2f.viewpos = view * world * vec4(in.Position, 1.0);
		gl_Position = projection * v2f.viewpos;
		v2f.PosW = (world * vec4(in.Position, 1.0)).xyz;
		v2f.Color = vertexColor;
		v2f.UV = in.TextureCoordinate;
		v2f.Normal = normalize((modelnormal * vec4(in.Normal, 1.0)).xyz);
		v2f.Light = lightoverride;
	}
}

// Slope handle shader
shader world3d_slope_handle extends world3d_vertex_color
{
	vertex
	{
		v2f.viewpos = view * world * vec4(in.Position.x * slopeHandleLength, in.Position.y, in.Position.z, 1.0);
		gl_Position = projection * v2f.viewpos;
		v2f.Color = in.Color * vertexColor;
		v2f.UV = in.TextureCoordinate;
	}
}


shader world3d_classic extends world3d_main
{
	fragment
	{
        vec4 pcolor;
		 
		if (bool(drawPaletted))
		{
			vec4 color = texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y));
		    int entry = int(color.r * 255);
		    float alpha = color.a;
		    int lightLevel = classicLightLevel(v2f.flatColor.rgb, v2f.Light);
            int colorMapOffset = classicLightLevelToColorMapOffset(lightLevel, v2f.PosW, v2f.flatNormal, false);
            pcolor = getColorMappedColor(entry, colorMapOffset);
            pcolor.a = alpha;
		}
		else
		{
            pcolor = texture(texture1, v2f.UV);
		}
		
		out.FragColor = pcolor;
		
		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif
	}
}

shader world3d_classic_highlight extends world3d_main
{
	fragment
	{
		vec4 pcolor;
		 
		if (bool(drawPaletted))
        {
			vec4 color = texture(texture1, v2f.UV + vec2(0.0, (v2f.UV.x - skew.x) * skew.y));
            int entry = int(color.r * 255);
            float alpha = color.a;
            int lightLevel = classicLightLevel(v2f.flatColor.rgb, v2f.Light);
            int modifiedLightLevel = max(lightLevel, 128);	 
            int colorMapOffset = classicLightLevelToColorMapOffset(modifiedLightLevel, v2f.PosW, v2f.flatNormal, false);
            pcolor = getColorMappedColor(entry, colorMapOffset);
            pcolor.a = alpha;
        }
        else
        {
            pcolor = texture(texture1, v2f.UV);
        }
		
		out.FragColor = pcolor;
		
		if (pcolor.a > 0.0)
		{
			out.FragColor = vec4(highlightcolor.rgb * highlightcolor.a + (pcolor.rgb - 0.4 * highlightcolor.a), max(pcolor.a + 0.25, 0.5));
		}
		
		#if defined(ALPHA_TEST)
		if (out.FragColor.a < 0.5) discard;
		#endif
	}
}
