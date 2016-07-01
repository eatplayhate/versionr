/*
*  LibXDiff by Davide Libenzi ( File Differential Library )
*  Copyright (C) 2003  Davide Libenzi
*
*  This library is free software; you can redistribute it and/or
*  modify it under the terms of the GNU Lesser General Public
*  License as published by the Free Software Foundation; either
*  version 2.1 of the License, or (at your option) any later version.
*
*  This library is distributed in the hope that it will be useful,
*  but WITHOUT ANY WARRANTY; without even the implied warranty of
*  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
*  Lesser General Public License for more details.
*
*  You should have received a copy of the GNU Lesser General Public
*  License along with this library; if not, write to the Free Software
*  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*
*  Davide Libenzi <davidel@xmailserver.org>
*
*/

#include <sys/types.h>
#include <sys/stat.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <fcntl.h>
#include <ctype.h>
#include "xdiff.h"
#include "xtestutils.h"

static int markfail(void *priv, mmbuffer_t *mb, int nbuf)
{
	*(int*)priv = 1;
	return 0;
}

static int xdlt_outf(void *priv, mmbuffer_t *mb, int nbuf) {
	int i;

	for (i = 0; i < nbuf; i++)
		if (!fwrite(mb[i].ptr, mb[i].size, 1, (FILE *)priv))
			return -1;

	return 0;
}


void usage(char const *prg) {

	fprintf(stderr,
		"use: %s --diff [-C N]   from-file  to-file\n"
		"     %s --patch         orig-file  patch-file\n"
		"     %s --bdiff [-B N]  from-file  to-file\n"
		"     %s --rabdiff       from-file  to-file\n"
		"     %s --bpatch        orig-file  patch-file\n",
		prg, prg, prg, prg, prg);
}


static void *wrap_malloc(void *priv, unsigned int size) {

	return malloc(size);
}


static void wrap_free(void *priv, void *ptr) {

	free(ptr);
}


static void *wrap_realloc(void *priv, void *ptr, unsigned int size) {

	return realloc(ptr, size);
}

void Init()
{
	memallocator_t malt;

	malt.priv = NULL;
	malt.malloc = wrap_malloc;
	malt.free = wrap_free;
	malt.realloc = wrap_realloc;
	xdl_set_allocator(&malt);
}

int __declspec(dllexport) GeneratePatch(const char* f1, const char* f2, const char* out)
{
	mmfile_t mf1, mf2;
	xpparam_t xpp;
	xdemitconf_t xecfg;
	bdiffparam_t bdp;
	xdemitcb_t ecb, rjecb;
	xecfg.ctxlen = 3;

	Init();

	xpp.flags = 0;
	if (xdlt_load_mmfile(f1, &mf1, 1) < 0) {
		return 1;
	}
	if (xdlt_load_mmfile(f2, &mf2, 1) < 0) {
		xdl_free_mmfile(&mf1);
		return 1;
	}

	FILE* f = fopen(out, "wb");
	ecb.priv = f;
	ecb.outf = xdlt_outf;

	if (xdl_diff(&mf1, &mf2, &xpp, &xecfg, &ecb) < 0) {
		fclose(f);

		xdl_free_mmfile(&mf2);
		xdl_free_mmfile(&mf1);
		return 2;
	}

	fclose(f);

	xdl_free_mmfile(&mf2);
	xdl_free_mmfile(&mf1);
	return 0;
}

int __declspec(dllexport) ApplyPatch(const char* f1, const char* f2, const char* out, const char* errors, int reverse)
{
	mmfile_t mf1, mf2;
	xpparam_t xpp;
	xdemitconf_t xecfg;
	bdiffparam_t bdp;
	xdemitcb_t ecb, rjecb;
	int mode;

	if (reverse == 1)
		mode = XDL_PATCH_REVERSE;
	else
		mode = XDL_PATCH_NORMAL;

	Init();

	xpp.flags = 0;
	if (xdlt_load_mmfile(f1, &mf1, 1) < 0) {
		return 1;
	}
	if (xdlt_load_mmfile(f2, &mf2, 1) < 0) {
		xdl_free_mmfile(&mf1);
		return 1;
	}

	FILE* f = fopen(out, "wb");
	FILE* e = fopen(errors, "wb");
	ecb.priv = f;
	ecb.outf = xdlt_outf;
	rjecb.priv = e;
	rjecb.outf = xdlt_outf;
	if (xdl_patch(&mf1, &mf2, mode, &ecb, &rjecb) < 0) {
		fclose(f);
		fclose(e);
		xdl_free_mmfile(&mf2);
		xdl_free_mmfile(&mf1);
		return 2;
	}

	fclose(f);
	fclose(e);

	xdl_free_mmfile(&mf2);
	xdl_free_mmfile(&mf1);
	return 0;
}

int __declspec(dllexport) GenerateBinaryPatch(const char* f1, const char* f2, const char* out)
{
	mmfile_t mf1, mf2;
	xpparam_t xpp;
	xdemitconf_t xecfg;
	bdiffparam_t bdp;
	xdemitcb_t ecb, rjecb;

	Init();

	xpp.flags = 0;
	if (xdlt_load_mmfile(f1, &mf1, 1) < 0) {
		return 1;
	}
	if (xdlt_load_mmfile(f2, &mf2, 1) < 0) {
		xdl_free_mmfile(&mf1);
		return 1;
	}

	FILE* f = fopen(out, "wb");
	ecb.priv = f;
	ecb.outf = xdlt_outf;

	if (xdl_rabdiff(&mf1, &mf2, &ecb) < 0) {
		fclose(f);

		xdl_free_mmfile(&mf2);
		xdl_free_mmfile(&mf1);
		return 2;
	}

	fclose(f);

	xdl_free_mmfile(&mf2);
	xdl_free_mmfile(&mf1);
	return 0;
}

int __declspec(dllexport) ApplyBinaryPatch(const char* f1, const char* f2, const char* out)
{
	mmfile_t mf1, mf2;
	xpparam_t xpp;
	xdemitconf_t xecfg;
	bdiffparam_t bdp;
	xdemitcb_t ecb, rjecb;

	Init();

	xpp.flags = 0;
	if (xdlt_load_mmfile(f1, &mf1, 1) < 0) {
		return 1;
	}
	if (xdlt_load_mmfile(f2, &mf2, 1) < 0) {
		xdl_free_mmfile(&mf1);
		return 1;
	}

	FILE* f = fopen(out, "wb");
	ecb.priv = f;
	ecb.outf = xdlt_outf;
	if (xdl_bpatch(&mf1, &mf2, &ecb) < 0) {
		fclose(f);
		xdl_free_mmfile(&mf2);
		xdl_free_mmfile(&mf1);
		return 2;
	}

	fclose(f);

	xdl_free_mmfile(&mf2);
	xdl_free_mmfile(&mf1);
	return 0;
}