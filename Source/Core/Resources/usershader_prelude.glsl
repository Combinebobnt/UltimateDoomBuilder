// Legacy material API adapted from UZDoom/GZDoom shaders and compatibility patcher.
// Copyright GZDoom and UZDoom Maintainers and Contributors. GPL-3.0-or-later.
in vec4 pixelpos, vTexCoord, viewpos, Color, vSectorLight;
in vec3 PosW, Normal;
out vec4 FragColor;
uniform float timer, fogEnabled, useVertexColorUniform, disableAlphaTest;
uniform sampler2D texture4, texture5, texture6, texture7, texture8, texture9, texture10;
#define texture2D texture
#define tex texture1
#define brighttexture texture3
#define camerapos (campos.xzy)
#define uCameraPos vec4(camerapos, 0.0)
#define uObjectColor vec4(1.0)
#define uDesaturationFactor desaturation
#define uTextureMode 0
#if defined(NORMALMAP)
// Layer samplers on units 10-13, after the seven custom shader textures
uniform sampler2D texture11, texture12, texture13, texture14;
uniform vec2 specularMaterial;
#define uSpecularMaterial specularMaterial
#define normaltexture texture11
#if defined(SPECULAR)
#define speculartexture texture12
#elif defined(PBR)
#define metallictexture texture12
#define roughnesstexture texture13
#define aotexture texture14
#endif
#else
#define uSpecularMaterial vec2(1.0)
#endif
#define vWorldNormal vec4(Normal.xzy, 0.0)
#define vEyeNormal vec4((view * vec4(Normal, 0.0)).xzy, 0.0)
// The lit vertex colour, as the baked colour was before the light moved into the shader
vec4 udbVColor;
#define vColor udbVColor
#define glowdist vec3(0.0)
#define gradientdist vec3(0.0)
struct Material
{
    vec4 Base, Bright, Glow;
    vec3 Normal, Specular;
    float Glossiness, SpecularLevel, Metallic, Roughness, AO;
};
vec4 getTexel(vec2 uv) { return desaturatePart(texture(texture1, uv)); }
vec4 editorBright(vec2 uv) { return brightmapEnabled > 0.0 ? desaturate(texture(texture3, uv)) : vec4(0.0); }
#if defined(NORMALMAP)
// UZDoom main.fp's screen-space tangent frame, run in its GL space (pixelpos and vWorldNormal)
mat3 cotangent_frame(vec3 n, vec3 p, vec2 uv)
{
    vec3 dp1 = dFdx(p);
    vec3 dp2 = dFdy(p);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);
    vec3 dp2perp = cross(n, dp2);
    vec3 dp1perp = cross(dp1, n);
    vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;
    float invmax = inversesqrt(max(dot(t, t), dot(b, b)));
    return mat3(t * invmax, b * invmax, n);
}
vec3 ApplyNormalMap(vec2 uv)
{
    vec3 interpolatedNormal = normalize(vWorldNormal.xyz);
    vec3 map = texture(normaltexture, uv).xyz;
    map = map * 255.0 / 127.0 - 128.0 / 127.0;
    map.y = -map.y;
    mat3 tbn = cotangent_frame(interpolatedNormal, pixelpos.xyz, vTexCoord.st);
    return normalize(tbn * map);
}
#else
vec3 ApplyNormalMap(vec2 uv) { return normalize(Normal.xzy); }
#endif
void SetMaterialProps(inout Material material, vec2 uv)
{
    material.Base = getTexel(uv);
    material.Bright = editorBright(uv);
    material.Normal = ApplyNormalMap(uv);
}
