// Specular and PBR light models adapted from UZDoom's material_specular.fp and material_pbr.fp.
// Copyright 2013-2016 Christoph Oelckers, GZDoom and UZDoom Maintainers and Contributors. GPL-3.0-or-later.
// The editor's lights are in Doom space with their class in lightColor.a; the maths runs there too,
// with the bumped normal swizzled back from GL space (dot products don't change under the swizzle).

// UDB's attenuation (distance, spot, the attenuated light's N.L) with the bumped normal for N.L
float editorLightPower(int i, vec3 bumped)
{
    return dynLightPower(PosW, Normal, bumped, lightColor[i], lightPosAndRadius[i], lightOrientation[i], light2Radius[i], lightStrengthAndLinearity[i]);
}

#if defined(SPECULAR)
vec3 ProcessMaterialLight(Material material, vec3 color)
{
    vec3 dynlight = vec3(0.0);
    vec3 specular = vec3(0.0);
    vec3 addlight = vec3(0.0);
    bool additive = false;
    vec3 normal = normalize(material.Normal.xzy);
    vec3 viewdir = normalize(campos.xyz - PosW);
    if (lightsEnabled != 0.0)
    {
        for (int i = 0; i < 64; i++)
        {
            if (lightColor[i].a == 0.0) break;
            float attenuation = editorLightPower(i, normal);
            if (lightColor[i].a < 0.4)
            {
                additive = true;
                addlight += lightColor[i].rgb * attenuation;
                continue;
            }
            if (attenuation <= 0.0) continue;
            vec3 halfdir = normalize(viewdir + normalize(lightPosAndRadius[i].xyz - PosW));
            float specAngle = clamp(dot(halfdir, normal), 0.0, 1.0);
            float highlight = attenuation * material.SpecularLevel * pow(specAngle, material.Glossiness * 4.0);
            // Subtractive lights take their light away, the rest are modulated
            float direction = lightColor[i].a >= 1.0 ? -1.0 : 1.0;
            dynlight += direction * lightColor[i].rgb * attenuation;
            specular += direction * lightColor[i].rgb * highlight;
        }
    }
    dynlight = clamp(color + desaturatePart(vec4(dynlight, 1.0)).rgb, 0.0, 1.4);
    specular = clamp(desaturatePart(vec4(specular, 1.0)).rgb, 0.0, 1.4);
    vec3 frag = material.Base.rgb * dynlight + material.Specular * specular;
    if (additive) frag = clamp(frag + desaturatePart(vec4(addlight, 1.0)).rgb, 0.0, 1.0);
    return frag;
}
#elif defined(PBR)
const float PI = 3.14159265359;

float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float denom = (NdotH * NdotH * (a2 - 1.0) + 1.0);
    return a2 / (PI * denom * denom);
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    return GeometrySchlickGGX(max(dot(N, L), 0.0), roughness) * GeometrySchlickGGX(max(dot(N, V), 0.0), roughness);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(1.0 - cosTheta, 5.0);
}

// UZDoom's PBR model has no additive lights, so they are dropped here too
vec3 ProcessMaterialLight(Material material, vec3 ambientLight)
{
    vec3 albedo = pow(material.Base.rgb, vec3(2.2));
    ambientLight = pow(ambientLight, vec3(2.2));
    float metallic = material.Metallic;
    float roughness = material.Roughness;
    vec3 N = normalize(material.Normal.xzy);
    vec3 V = normalize(campos.xyz - PosW);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 Lo = vec3(0.0);
    if (lightsEnabled != 0.0)
    {
        for (int i = 0; i < 64; i++)
        {
            if (lightColor[i].a == 0.0) break;
            if (lightColor[i].a < 0.4) continue;
            float attenuation = editorLightPower(i, N);
            if (attenuation <= 0.0) continue;
            vec3 L = normalize(lightPosAndRadius[i].xyz - PosW);
            vec3 H = normalize(V + L);
            vec3 radiance = lightColor[i].rgb * attenuation;
            float NDF = DistributionGGX(N, H, roughness);
            float G = GeometrySmith(N, V, L, roughness);
            vec3 F = fresnelSchlick(clamp(dot(H, V), 0.0, 1.0), F0);
            vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
            float denominator = 4.0 * clamp(dot(N, V), 0.0, 1.0) * clamp(dot(N, L), 0.0, 1.0);
            vec3 specular = NDF * G * F / max(denominator, 0.001);
            Lo += (lightColor[i].a >= 1.0 ? -1.0 : 1.0) * (kD * albedo / PI + specular) * radiance;
        }
    }
    vec3 kD = 1.0 - fresnelSchlickRoughness(clamp(dot(N, V), 0.0, 1.0), F0, roughness);
    vec3 ambient = (kD * ambientLight * albedo) * material.AO;
    return pow(max(ambient + Lo, vec3(0.0)), vec3(1.0 / 2.2));
}
#endif
