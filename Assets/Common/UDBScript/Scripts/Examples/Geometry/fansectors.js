/// <reference path="../../../udbscript.d.ts" />

`#version 4`;

`#name Fan Sectors`;

`#description Splits each selected or highlighted sector into triangles that all share one corner, for vertex-height terrain. With one sector, run it from a script slot hotkey to fan from the corner nearest the mouse; otherwise the origin option picks the corner. Rejects sectors with holes, self-intersecting outlines and Plane_Align or Plane_Copy lines. Slope things and Doom-format slope specials are not checked: convert such slopes first.`;

`#scriptoptions

origin
{
	description = "Fan origin when not picked with the mouse";
	type = 11; // Enum
	default = 0;
	enumvalues {
		0 = "Best (largest smallest angle)";
		1 = "First valid corner (lowest vertex index)";
	}
}
`;

const fmt = p => '(' + p.x + ', ' + p.y + ')';

const sectors = UDB.Map.getSelectedOrHighlightedSectors();
if(sectors.length == 0)
	UDB.die('No sectors selected or highlighted');

const mouse = UDB.Map.mousePosition;
const useMouse = sectors.length == 1 && mouse.isFinite();
const slopeActions = UDB.Map.isDoom ? [] : [ 181, 118 ]; // Plane_Align, Plane_Copy

// Work out every fan from positions first; sectors split while drawing
const fans = [];
for(const s of sectors)
{
	const traced = SectorMesh.traceSectorLoop(s, slopeActions);
	if(traced.error)
		UDB.die('Sector ' + s.index + ': ' + traced.error);
	const loop = SectorMesh.cleanLoop(traced.loop);
	if(!SectorMesh.isSimple(loop))
		UDB.die('Sector ' + s.index + ': its outline intersects itself');
	if(loop.length < 4)
	{
		if(traced.loop.length > 3)
			UDB.log('Sector ' + s.index + ' has three corners but ' + traced.loop.length + ' vertices, skipped. Merge the vertices on its straight edges away so vertex heights apply.');
		else
			UDB.log('Sector ' + s.index + ' is already a triangle, skipped');
		continue;
	}

	let origin;
	if(useMouse)
	{
		origin = 0;
		loop.forEach((p, i) => { if(Math.hypot(p.x - mouse.x, p.y - mouse.y) < Math.hypot(loop[origin].x - mouse.x, loop[origin].y - mouse.y)) origin = i; });
		const fan = SectorMesh.fanDiagonals(loop, origin);
		if(!fan.ok)
		{
			const best = SectorMesh.bestFanOrigin(loop);
			UDB.die('Sector ' + s.index + ': can not fan from ' + fmt(loop[origin]) + ', ' + fan.reason +
				(best === null ? '. No corner can fan it.' : '. Try the corner at ' + fmt(loop[best]) + '.'));
		}
		UDB.log('Sector ' + s.index + ': fanning from the corner nearest the mouse, ' + fmt(loop[origin]));
	}
	else
	{
		const valid = SectorMesh.validFanOrigins(loop);
		if(valid.length == 0)
			UDB.die('Sector ' + s.index + ': no corner can fan it (it is not star-shaped from any corner)');
		if(UDB.ScriptOptions.origin == 1)
			origin = valid.reduce((a, b) => loop[b.index].index < loop[a.index].index ? b : a).index;
		else
			origin = SectorMesh.bestFanOrigin(loop);
		UDB.log('Sector ' + s.index + ': origin option "' + (UDB.ScriptOptions.origin == 1 ? 'first valid' : 'best') + '" picked the corner at ' + fmt(loop[origin]));
	}

	const fan = SectorMesh.fanDiagonals(loop, origin);
	fan.skipped.forEach(w => UDB.log('Sector ' + s.index + ': the triangle toward ' + fmt(loop[w]) + ' keeps a straight-through vertex, so vertex heights will not apply to it until that vertex is merged away'));
	traced.loop.filter(p => !loop.includes(p)).forEach(p => UDB.log('Sector ' + s.index + ': vertex ' + p.index + ' at ' + fmt(p) + ' is on a straight edge, so the triangle it ends up in has four vertices'));
	if(UDB.Map.isUDMF)
		traced.loop.forEach(p => {
			if(!isNaN(p.vertex.floorZ) || !isNaN(p.vertex.ceilingZ))
				UDB.log('Sector ' + s.index + ': vertex ' + p.index + ' already has a floor or ceiling height set, which now applies to the new triangles');
		});

	fan.diagonals.forEach(w => fans.push([ [ loop[origin].x, loop[origin].y ], [ loop[w].x, loop[w].y ] ]));
}

for(const line of fans)
	if(!UDB.Map.drawLines(line))
		UDB.die('Drawing the line from ' + line[0] + ' to ' + line[1] + ' failed');
