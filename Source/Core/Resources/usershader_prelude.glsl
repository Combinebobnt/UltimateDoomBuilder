// Legacy material API adapted from UZDoom/GZDoom shaders and compatibility patcher.
// Copyright GZDoom and UZDoom Maintainers and Contributors. GPL-3.0-or-later.
in vec4 pixelpos, vTexCoord, viewpos, Color;
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
#define uSpecularMaterial vec2(1.0)
#define vWorldNormal vec4(Normal.xzy, 0.0)
#define vEyeNormal vec4((view * vec4(Normal, 0.0)).xzy, 0.0)
#define vColor Color
#define glowdist vec3(0.0)
#define gradientdist vec3(0.0)
struct Material
{
    vec4 Base, Bright, Glow;
    vec3 Normal, Specular;
    float Glossiness, SpecularLevel, Metallic, Roughness, AO;
};
vec4 getTexel(vec2 uv) { return texture(texture1, uv); }
vec4 editorBright(vec2 uv) { return brightmapEnabled > 0.0 ? desaturate(texture(texture3, uv)) : vec4(0.0); }
vec3 ApplyNormalMap(vec2 uv) { return normalize(Normal.xzy); }
void SetMaterialProps(inout Material material, vec2 uv)
{
    material.Base = getTexel(uv);
    material.Bright = editorBright(uv);
    material.Normal = ApplyNormalMap(uv);
}
