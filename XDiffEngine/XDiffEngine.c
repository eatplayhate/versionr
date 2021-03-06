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

#ifdef _MSC_VER
#define XDIFF_EXPORT __declspec(dllexport)
#else
#define XDIFF_EXPORT __attribute__ ((visibility ("default")))
#endif

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

int XDIFF_EXPORT Merge3Way(const char* base, const char* f1, const char* f2, const char* out)
{
	mmfile_t mf1, mf2, mfb;
	xpparam_t xpp;
	xdemitconf_t xecfg;
	bdiffparam_t bdp;
	xdemitcb_t ecb, rjecb;
	int status = 0;
	xecfg.ctxlen = 3;

	Init();

	xpp.flags = 0;
	if (xdlt_load_mmfile(base, &mfb, 1) < 0) {
		return 1;
	}
	if (xdlt_load_mmfile(f1, &mf1, 1) < 0) {
		xdl_free_mmfile(&mfb);
		return 1;
	}
	if (xdlt_load_mmfile(f2, &mf2, 1) < 0) {
		xdl_free_mmfile(&mf1);
		xdl_free_mmfile(&mfb);
		return 1;
	}

	FILE* f = fopen(out, "wb");
	ecb.priv = f;
	ecb.outf = xdlt_outf;
	rjecb.priv = &status;
	rjecb.outf = markfail;

	if (xdl_merge3(&mfb, &mf1, &mf2, &ecb, &rjecb) < 0) {
		fclose(f);

		xdl_free_mmfile(&mf2);
		xdl_free_mmfile(&mf1);
		xdl_free_mmfile(&mfb);
		return 2;
	}

	fclose(f);

	xdl_free_mmfile(&mf2);
	xdl_free_mmfile(&mf1);
	xdl_free_mmfile(&mfb);
	return status;
}


int XDIFF_EXPORT GeneratePatch(const char* f1, const char* f2, const char* out)
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

int XDIFF_EXPORT ApplyPatch(const char* f1, const char* f2, const char* out, const char* errors, int reverse, int flags)
{
	mmfile_t mf1, mf2;
	xpparam_t xpp;
	xdemitconf_t xecfg;
	bdiffparam_t bdp;
	xdemitcb_t ecb, rjecb;
	int mode;

	if (reverse == 1)
		mode = XDL_PATCH_REVERSE | flags;
	else
		mode = XDL_PATCH_NORMAL | flags;

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

int XDIFF_EXPORT GenerateBinaryPatch(const char* f1, const char* f2, const char* out)
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

int XDIFF_EXPORT ApplyBinaryPatch(const char* f1, const char* f2, const char* out)
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
