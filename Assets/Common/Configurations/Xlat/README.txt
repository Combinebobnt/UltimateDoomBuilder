Doom-format line and sector translation tables

These files are copied unchanged from UZDoom (https://github.com/UZDoom/UZDoom),
branch trunk at commit 58625a71e4c53ab22271496278efb84ecfe99692:

  wadsrc/static/xlat/base.txt, defines.i, doom.txt, doom_base.txt,
  heretic.txt, heretic_base.txt, strife.txt, strife_base.txt
  src/playsim/actionspecials.h

They are the tables ZDoom-family engines use to translate Doom-format maps
into Hexen-style specials when loading them. Ultimate Doom Builder reads them
to convert maps between the Doom, Hexen and UDMF formats (Map > Convert Map
Format), so a converted map behaves the way the engine reads the original.
actionspecials.h supplies the special numbers for the names the tables use.

Copyright 1998-2016 Marisa Heit, 2005-2016 Christoph Oelckers,
2017-2025 GZDoom Maintainers and Contributors,
2025-2026 UZDoom Maintainers and Contributors.
Licensed GPL-3.0-or-later (code written before 2026 also BSD-3-Clause).
