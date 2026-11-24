/// <reference path="../../../udbscript.d.ts" />

`#version 4`;

`#name Subdivide Quad Sectors`;

`#description Splits each selected or highlighted four-cornered convex sector into a grid of quads, optionally cutting every cell into two triangles, for vertex-height terrain. Columns run along the edge from the corner with the lowest vertex index to the next corner counter-clockwise. In UDMF, when any corner has a vertex floor or ceiling height, the new vertices get heights blended from the corners. Rejects Plane_Align and Plane_Copy lines; slope things and Doom-format slope specials are not checked, so convert such slopes first.`;

`#scriptoptions

columns
{
	description = "Columns";
	default = 2;
	type = 0; // Integer
}

rows
{
	description = "Rows";
	default = 2;
	type = 0; // Integer
}

diagonal
{
	description = "Cell diagonal";
	type = 11; // Enum
	default = 3;
	enumvalues {
		0 = "None (quads)";
		1 = "A (corner 0 to corner 2 of each cell)";
		2 = "B (corner 1 to corner 3 of each cell)";
		3 = "Alternate (checkerboard)";
		4 = "Shorter";
	}
}

heights
{
	description = "Vertex heights (UDMF)";
	type = 11; // Enum
	default = 0;
	enumvalues {
		0 = "Interpolate from the corners";
		1 = "Leave unset";
	}
}
`;

const MAX_CELLS = 64;
const MIN_CELL_SIZE = 2;
const DIAGONAL_MODES = [ 'none', 'a', 'b', 'alternate', 'shorter' ];
const fmt = p => '(' + p.x + ', ' + p.y + ')';

const cols = UDB.ScriptOptions.columns, rows = UDB.ScriptOptions.rows;
if(cols < 1 || rows < 1 || cols > MAX_CELLS || rows > MAX_CELLS)
	UDB.die('Columns and rows must be between 1 and ' + MAX_CELLS);
const diagonal = DIAGONAL_MODES[UDB.ScriptOptions.diagonal];
if(cols == 1 && rows == 1 && diagonal == 'none')
	UDB.die('One column and one row without a diagonal changes nothing');

const sectors = UDB.Map.getSelectedOrHighlightedSectors();
if(sectors.length == 0)
	UDB.die('No sectors selected or highlighted');

const targets = new Set(sectors.map(s => s.index));
const slopeActions = UDB.Map.isDoom ? [] : [ 181, 118 ]; // Plane_Align, Plane_Copy
const interpolate = UDB.Map.isUDMF && UDB.ScriptOptions.heights == 0;

// Corner heights for bilinear blending, or null when no corner has one set. An unset corner uses the
// flat height, as GZDoom does for unset vertices of a vertex-height triangle.
function cornerHeights(s, corners, ceiling)
{
	const set = corners.map(c => ceiling ? c.vertex.ceilingZ : c.vertex.floorZ);
	if(set.every(z => isNaN(z)))
		return null;
	return set.map(z => isNaN(z) ? (ceiling ? s.ceilingHeight : s.floorHeight) : z);
}

function distToSegment(p, a, b)
{
	const dx = b.x - a.x, dy = b.y - a.y;
	const t = Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / (dx * dx + dy * dy)));
	return Math.hypot(p.x - a.x - t * dx, p.y - a.y - t * dy);
}

function hasVertexHeights(s)
{
	return s.getSidedefs().some(sd => [ sd.line.start, sd.line.end ].some(v => !isNaN(v.floorZ) || !isNaN(v.ceilingZ)));
}

function smallestCell(grid)
{
	let min = Infinity;
	for(let i = 0; i <= cols; i++)
		for(let j = 0; j <= rows; j++)
		{
			const p = grid[i][j];
			if(i < cols)
				min = Math.min(min, Math.hypot(grid[i + 1][j].x - p.x, grid[i + 1][j].y - p.y));
			if(j < rows)
				min = Math.min(min, Math.hypot(grid[i][j + 1].x - p.x, grid[i][j + 1].y - p.y));
		}
	return min;
}

// Work everything out from positions first; sectors split while drawing
const plans = [];
const slopedNeighbours = new Set();
for(const s of sectors)
{
	const traced = SectorMesh.traceSectorLoop(s, slopeActions);
	if(traced.error)
		UDB.die('Sector ' + s.index + ': ' + traced.error);
	const raw = traced.loop;
	let loop = SectorMesh.cleanLoop(raw);
	if(!SectorMesh.isSimple(loop))
		UDB.die('Sector ' + s.index + ': its outline intersects itself');
	if(loop.length != 4)
		UDB.die('Sector ' + s.index + ': has ' + loop.length + ' corners, needs exactly 4');
	if(!SectorMesh.isConvex(loop))
		UDB.die('Sector ' + s.index + ': is a concave quad');

	// Corner 0 is the one with the lowest vertex index, the rest follow counter-clockwise
	let first = 0;
	loop.forEach((p, i) => { if(p.index < loop[first].index) first = i; });
	const corners = loop.slice(first).concat(loop.slice(0, first));

	const grid = SectorMesh.quadGrid(corners, cols, rows, !UDB.Map.isUDMF);
	if(smallestCell(grid) < MIN_CELL_SIZE)
		UDB.die('Sector ' + s.index + ': cells would be smaller than ' + MIN_CELL_SIZE + ' map units, use fewer columns or rows');
	const shared = [];
	for(const p of grid.flat())
	{
		p.existing = p.corner = corners.some(c => c.x == p.x && c.y == p.y);
		if(!p.boundary || p.existing)
			continue;
		// Snap to an existing boundary vertex nearby, so no sliver segments are left
		const near = raw.find(r => Math.hypot(r.x - p.x, r.y - p.y) <= 1);
		if(near !== undefined)
		{
			p.x = near.x;
			p.y = near.y;
			p.existing = true;
			continue;
		}
		const edge = raw.findIndex((r, k) => distToSegment(p, r, raw[(k + 1) % raw.length]) < 1);
		const other = edge < 0 ? null : raw[edge].other;
		if(other === null)
			continue;
		if(targets.has(other.index))
			shared.push({ other: other.index, p: p });
		// A three-sided neighbour with vertex heights would become four-sided and lose its slope
		else if(UDB.Map.isUDMF && other.getSidedefs().length == 3 && hasVertexHeights(other))
			slopedNeighbours.add(other.index);
	}

	plans.push({
		index: s.index,
		grid: grid,
		shared: shared,
		floor: interpolate ? cornerHeights(s, corners, false) : null,
		ceiling: interpolate ? cornerHeights(s, corners, true) : null
	});
}

// Selected quads sharing an edge must split it at the same points, or their edge triangles get extra vertices
for(const plan of plans)
	for(const sp of plan.shared)
	{
		const other = plans.find(o => o.index == sp.other);
		if(!other.grid.flat().some(q => q.boundary && Math.hypot(q.x - sp.p.x, q.y - sp.p.y) <= 1))
			UDB.die('Sectors ' + plan.index + ' and ' + other.index + ' would split their shared edge at different points ' +
				'(columns run from the corner with the lowest vertex index). Subdivide them separately with matching counts along that edge.');
	}

if(slopedNeighbours.size > 0 && !UDB.showMessageYesNo('These triangular sectors have vertex heights and would get a new vertex on a shared edge, which turns off their vertex-height slope: ' +
	[...slopedNeighbours].join(', ') + '.\n\nSubdivide anyway?'))
	UDB.die('Cancelled, nothing was changed');

const xy = p => [ p.x, p.y ];

// Moves boundary points onto the vertex UDB made or already had there (rounded in Doom and Hexen maps)
function settle(points)
{
	for(const p of points)
	{
		if(!p.boundary || p.corner)
			continue;
		const v = UDB.Map.nearestVertex(xy(p), 1);
		if(v !== null)
		{
			p.x = v.position.x;
			p.y = v.position.y;
		}
	}
}

function draw(points)
{
	settle(points);
	if(!UDB.Map.drawLines(points.map(xy)))
		UDB.die('Drawing the line from ' + fmt(points[0]) + ' to ' + fmt(points[points.length - 1]) + ' failed');
	settle(points);
}

for(const plan of plans)
{
	const g = plan.grid;
	for(let i = 1; i < cols; i++)
		draw(g[i]);
	for(let j = 1; j < rows; j++)
		draw(g.map(column => column[j]));
	for(let i = 0; i < cols; i++)
		for(let j = 0; j < rows; j++)
		{
			const d = SectorMesh.cellDiagonal(g, i, j, diagonal);
			if(d !== null)
				draw(d);
		}

	if(plan.floor === null && plan.ceiling === null)
		continue;
	g.forEach(column => column.forEach(p => {
		if(p.existing)
			return;
		const v = UDB.Map.nearestVertex(xy(p), 0.5);
		if(v === null)
		{
			UDB.log('Sector ' + plan.index + ': no vertex found at ' + fmt(p) + ', its height was not set');
			return;
		}
		if(plan.floor !== null)
			v.floorZ = SectorMesh.bilinear(plan.floor, p.u, p.v);
		if(plan.ceiling !== null)
			v.ceilingZ = SectorMesh.bilinear(plan.ceiling, p.u, p.v);
	}));
}
