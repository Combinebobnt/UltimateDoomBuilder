/*
** Adapted from UZDoom's func_warp1.fp to the Zandronum Process API.
** Copyright 2013-2016 Christoph Oelckers
** Copyright 2017-2025 GZDoom Maintainers and Contributors
** Copyright 2025-2026 UZDoom Maintainers and Contributors
** SPDX-License-Identifier: GPL-3.0-or-later
*/

vec4 Process(vec4 color)
{
	vec2 texCoord = vTexCoord.st;
	const float pi = 3.14159265358979323846;
	vec2 offset = vec2(0,0);
	offset.y = sin(pi * 2.0 * (texCoord.x + timer * 0.125)) * 0.1;
	offset.x = sin(pi * 2.0 * (texCoord.y + timer * 0.125)) * 0.1;
	return getTexel(texCoord + offset) * color;
}
