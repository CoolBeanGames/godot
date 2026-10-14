/**************************************************************************/
/*  crete_steam_stub.cpp                                                  */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             CRETE ENGINE                               */
/**************************************************************************/
/* Copyright (c) 2026 Crete contributors.                                 */
/*                                                                        */
/* Permission is hereby granted, free of charge, to any person obtaining  */
/* a copy of this software and associated documentation files (the        */
/* "Software"), to deal in the Software without restriction, including    */
/* without limitation the rights to use, copy, modify, merge, publish,    */
/* distribute, sublicense, and/or sell copies of the Software, and to     */
/* permit persons to whom the Software is furnished to do so, subject to  */
/* the following conditions:                                              */
/*                                                                        */
/* The above copyright notice and this permission notice shall be         */
/* included in all copies or substantial portions of the Software.        */
/*                                                                        */
/* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,        */
/* EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF     */
/* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. */
/* IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY   */
/* CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,   */
/* TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE      */
/* SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.                 */
/**************************************************************************/

#include "crete_steam_stub.h"

#include "core/error/error_macros.h"
#include "core/object/class_db.h"

Steam *Steam::singleton = nullptr;

Steam *Steam::get_singleton() {
	return singleton;
}

Steam::Steam() {
	singleton = this;
}

Steam::~Steam() {
	singleton = nullptr;
}

bool Steam::isSteamRunning() {
	return false;
}

bool Steam::restartAppIfNecessary(uint32_t p_app_id) {
	(void)p_app_id;
	return false;
}

Dictionary Steam::steamInitEx(uint32_t p_app_id, bool p_embed_callbacks) {
	(void)p_app_id;
	(void)p_embed_callbacks;
	WARN_PRINT("Steam API is stubbed in this build (no Steamworks SDK).");
	Dictionary ret;
	// Same shape as the real module: status 1 == FAILED_GENERIC.
	ret["status"] = 1;
	ret["verbal"] = "Crete build without Steamworks SDK: Steam API is stubbed.";
	return ret;
}

uint32_t Steam::getAppID() {
	return 0;
}

bool Steam::isCreteStub() {
	return true;
}

void Steam::_bind_methods() {
	ClassDB::bind_method(D_METHOD("isSteamRunning"), &Steam::isSteamRunning);
	ClassDB::bind_method(D_METHOD("restartAppIfNecessary", "app_id"), &Steam::restartAppIfNecessary);
	ClassDB::bind_method(D_METHOD("steamInitEx", "app_id", "embed_callbacks"), &Steam::steamInitEx, DEFVAL(0), DEFVAL(false));
	ClassDB::bind_method(D_METHOD("getAppID"), &Steam::getAppID);
	ClassDB::bind_method(D_METHOD("isCreteStub"), &Steam::isCreteStub);
}
