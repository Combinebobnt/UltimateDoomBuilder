// Pure polygon helpers used by the "Fan Sectors" and "Subdivide Quad Sectors" example scripts.
// Points are plain objects with x and y; extra properties (like a vertex index) are kept.
var SectorMesh = (function()
{
	const EPS = 1e-6;

	function cross(o, a, b)
	{
		return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
	}

	function dist(a, b)
	{
		return Math.hypot(b.x - a.x, b.y - a.y);
	}

	function signedArea(loop)
	{
		let area = 0;
		for(let i = 0; i < loop.length; i++)
		{
			const a = loop[i], b = loop[(i + 1) % loop.length];
			area += a.x * b.y - b.x * a.y;
		}
		return area / 2;
	}

	// True when b is on the straight line from a to c and the boundary keeps going forward
	function isStraightOn(a, b, c)
	{
		const la = dist(a, b), lc = dist(b, c);
		if(la < EPS || lc < EPS)
			return true;
		const dot = (b.x - a.x) * (c.x - b.x) + (b.y - a.y) * (c.y - b.y);
		return Math.abs(cross(a, b, c)) <= EPS * la * lc && dot > 0;
	}

	// Drops duplicate and straight-through points and orients the loop counter-clockwise
	function cleanLoop(points)
	{
		let loop = points.slice();
		let changed = true;
		while(changed && loop.length > 2)
		{
			changed = false;
			for(let i = 0; i < loop.length && loop.length > 2; i++)
			{
				const a = loop[(i + loop.length - 1) % loop.length], b = loop[i], c = loop[(i + 1) % loop.length];
				if(isStraightOn(a, b, c))
				{
					loop.splice(i, 1);
					changed = true;
					i--;
				}
			}
		}
		if(signedArea(loop) < 0)
			loop.reverse();
		return loop;
	}

	function onSegment(p, a, b)
	{
		const len = dist(a, b);
		if(Math.abs(cross(a, b, p)) > EPS * Math.max(len, 1) * Math.max(len, 1))
			return false;
		return Math.min(a.x, b.x) - EPS <= p.x && p.x <= Math.max(a.x, b.x) + EPS &&
			Math.min(a.y, b.y) - EPS <= p.y && p.y <= Math.max(a.y, b.y) + EPS;
	}

	// Closed segment intersection, touching counts
	function segmentsIntersect(a, b, c, d)
	{
		const d1 = cross(c, d, a), d2 = cross(c, d, b), d3 = cross(a, b, c), d4 = cross(a, b, d);
		if(((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
			return true;
		return onSegment(a, c, d) || onSegment(b, c, d) || onSegment(c, a, b) || onSegment(d, a, b);
	}

	function isSimple(loop)
	{
		const n = loop.length;
		if(n < 3 || Math.abs(signedArea(loop)) < EPS)
			return false;
		for(let i = 0; i < n; i++)
		{
			const a = loop[i], b = loop[(i + 1) % n];
			// Adjacent edges may only share their common point
			const c = loop[(i + 2) % n];
			if(Math.abs(cross(a, b, c)) <= EPS * dist(a, b) * dist(b, c) &&
				(b.x - a.x) * (c.x - b.x) + (b.y - a.y) * (c.y - b.y) < 0)
				return false;
			for(let j = i + 2; j < n; j++)
			{
				if(i == 0 && j == n - 1)
					continue;
				if(segmentsIntersect(a, b, loop[j], loop[(j + 1) % n]))
					return false;
			}
		}
		return true;
	}

	// Expects a counter-clockwise loop
	function isConvex(loop)
	{
		const n = loop.length;
		for(let i = 0; i < n; i++)
			if(cross(loop[i], loop[(i + 1) % n], loop[(i + 2) % n]) <= 0)
				return false;
		return true;
	}

	function triangleMinAngle(a, b, c)
	{
		const la = dist(b, c), lb = dist(a, c), lc = dist(a, b);
		const angle = (opp, s1, s2) => Math.acos(Math.max(-1, Math.min(1, (s1 * s1 + s2 * s2 - opp * opp) / (2 * s1 * s2))));
		return Math.min(angle(la, lb, lc), angle(lb, la, lc), angle(lc, la, lb));
	}

	// Diagonals that fan a counter-clockwise loop from loop[o]. o must be in the polygon's kernel.
	// A diagonal running through another corner is skipped: the one to the nearer corner covers it.
	function fanDiagonals(loop, o)
	{
		const n = loop.length;
		const origin = loop[o];
		let minAngle = Infinity;

		for(let i = 0; i < n; i++)
		{
			const a = loop[i], b = loop[(i + 1) % n];
			if(i == o || (i + 1) % n == o)
				continue;
			const c = cross(a, b, origin);
			const scale = dist(a, b) * Math.max(dist(a, origin), dist(b, origin));
			if(c < -EPS * scale)
				return { ok: false, reason: 'corner (' + origin.x + ', ' + origin.y + ') can not see the edge from (' + a.x + ', ' + a.y + ') to (' + b.x + ', ' + b.y + ')' };
			if(c > EPS * scale)
				minAngle = Math.min(minAngle, triangleMinAngle(origin, a, b));
		}

		const diagonals = [], skipped = [];
		for(let k = 2; k < n - 1; k++)
		{
			const w = (o + k) % n;
			let through = false;
			for(let m = 0; m < n && !through; m++)
				if(m != o && m != w && onSegment(loop[m], origin, loop[w]))
					through = true;
			(through ? skipped : diagonals).push(w);
		}

		return { ok: true, diagonals: diagonals, skipped: skipped, minAngle: minAngle };
	}

	// Every corner the loop can be fanned from, with its smallest triangle angle
	function validFanOrigins(loop)
	{
		const result = [];
		for(let o = 0; o < loop.length; o++)
		{
			const fan = fanDiagonals(loop, o);
			if(fan.ok)
				result.push({ index: o, minAngle: fan.minAngle });
		}
		return result;
	}

	// The origin index that maximizes the smallest triangle angle, or null when none works
	function bestFanOrigin(loop)
	{
		let best = null;
		validFanOrigins(loop).forEach(v => { if(best === null || v.minAngle > best.minAngle + EPS) best = v; });
		return best === null ? null : best.index;
	}

	function bilinear(values, u, v)
	{
		return (1 - u) * (1 - v) * values[0] + u * (1 - v) * values[1] + u * v * values[2] + (1 - u) * v * values[3];
	}

	// Grid points P[i][j], i along corner 0 -> corner 1 (columns), j along corner 0 -> corner 3 (rows)
	function quadGrid(corners, cols, rows, round)
	{
		const grid = [];
		for(let i = 0; i <= cols; i++)
		{
			grid.push([]);
			for(let j = 0; j <= rows; j++)
			{
				const u = i / cols, v = j / rows;
				let x = bilinear(corners.map(c => c.x), u, v);
				let y = bilinear(corners.map(c => c.y), u, v);
				if(round)
				{
					x = Math.round(x);
					y = Math.round(y);
				}
				grid[i].push({ x: x, y: y, u: u, v: v });
			}
		}
		return grid;
	}

	// The diagonal of cell (i, j) as a pair of points, or null. Modes: none, a, b, alternate, shorter.
	function cellDiagonal(grid, i, j, mode)
	{
		const p00 = grid[i][j], p10 = grid[i + 1][j], p11 = grid[i + 1][j + 1], p01 = grid[i][j + 1];
		if(mode == 'alternate')
			mode = (i + j) % 2 == 0 ? 'a' : 'b';
		else if(mode == 'shorter')
			mode = dist(p10, p01) < dist(p00, p11) - EPS ? 'b' : 'a';
		if(mode == 'a')
			return [ p00, p11 ];
		if(mode == 'b')
			return [ p10, p01 ];
		return null;
	}

	// Traces a sector's boundary from its sidedefs. Returns { loop } or { error }. Each point carries
	// the vertex and its index, and for the edge to the next point its line and the sector on the other side.
	function traceSectorLoop(sector, planeAlignAction)
	{
		const edges = new Map();
		const sidedefs = sector.getSidedefs();
		for(const sd of sidedefs)
		{
			const line = sd.line;
			if(sd.other !== null && sd.other.sector !== null && sd.other.sector.index == sector.index)
				return { error: 'line ' + line.index + ' has the sector on both sides' };
			if(planeAlignAction !== null && line.action == planeAlignAction)
				return { error: 'line ' + line.index + ' has a Plane_Align slope, convert it to a plane-equation slope first' };
			const from = sd.isFront ? line.start : line.end, to = sd.isFront ? line.end : line.start;
			if(edges.has(from.index))
				return { error: 'its boundary touches itself at vertex ' + from.index };
			edges.set(from.index, { from: from, to: to, line: line, other: sd.other === null ? null : sd.other.sector });
		}
		if(edges.size == 0)
			return { error: 'has no sidedefs' };

		const loop = [];
		const first = edges.values().next().value;
		let edge = first;
		do
		{
			loop.push({ x: edge.from.position.x, y: edge.from.position.y, index: edge.from.index, vertex: edge.from, line: edge.line, other: edge.other });
			edge = edges.get(edge.to.index);
			if(edge === undefined)
				return { error: 'its boundary is not closed' };
		} while(edge !== first && loop.length <= edges.size);
		if(edge !== first || loop.length < edges.size)
			return { error: 'has a hole or more than one part' };
		return { loop: loop };
	}

	return {
		traceSectorLoop: traceSectorLoop,
		cleanLoop: cleanLoop,
		isSimple: isSimple,
		isConvex: isConvex,
		signedArea: signedArea,
		onSegment: onSegment,
		fanDiagonals: fanDiagonals,
		validFanOrigins: validFanOrigins,
		bestFanOrigin: bestFanOrigin,
		quadGrid: quadGrid,
		cellDiagonal: cellDiagonal,
		bilinear: bilinear
	};
})();

if(typeof module !== 'undefined' && module.exports)
	module.exports = SectorMesh;
