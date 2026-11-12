/*
** Adapted from UZDoom's func_warp2.fp to the Zandronum Process API.
** This uses UZDoom's software-compatible formula, not the older Zandronum one.
** Copyright 2013-2016 Christoph Oelckers
** Copyright 2017-2025 GZDoom Maintainers and Contributors
** Copyright 2025-2026 UZDoom Maintainers and Contributors
** SPDX-License-Identifier: GPL-3.0-or-later
*/

vec4 Process(vec4 color)
{
	vec2 texCoord = vTexCoord.st;
	const float pi = 3.14159265358979323846;
	vec2 offset = vec2(0.0,0.0);
	offset.y = 0.5 + sin(pi * 2.0 * (texCoord.y + timer * 0.61 + 900.0/8192.0)) + sin(pi * 2.0 * (texCoord.x * 2.0 + timer * 0.36 + 300.0/8192.0));
	offset.x = 0.5 + sin(pi * 2.0 * (texCoord.y + timer * 0.49 + 700.0/8192.0)) + sin(pi * 2.0 * (texCoord.x * 2.0 + timer * 0.49 + 1200.0/8192.0));
	return getTexel(texCoord + offset * 0.025) * color;
}
