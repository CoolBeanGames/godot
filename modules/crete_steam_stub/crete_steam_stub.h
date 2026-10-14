/**************************************************************************/
/*  crete_steam_stub.h                                                    */
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

#pragma once

#include "core/object/object.h"
#include "core/variant/dictionary.h"

// Minimal stand-in for GodotSteam's Steam singleton, compiled when the
// Steamworks SDK is absent (the SDK is not redistributable, so local builds
// never have it). Same class/singleton name and same method shapes as the
// real module, so `Steam.steamInitEx()` etc. resolve and fail honestly
// instead of erroring on a missing singleton. Anything beyond this subset
// needs an SDK build (see P4 CI). Mutually exclusive with modules/godotsteam.
class Steam : public Object {
	GDCLASS(Steam, Object);

	static Steam *singleton;

protected:
	static void _bind_methods();

public:
	static Steam *get_singleton();

	Steam();
	~Steam();

	bool isSteamRunning();
	bool restartAppIfNecessary(uint32_t app_id);
	Dictionary steamInitEx(uint32_t app_id = 0, bool embed_callbacks = false);
	uint32_t getAppID();

	// Marker the real module never has: lets tests (and game code) tell
	// stub builds from SDK builds at runtime.
	bool isCreteStub();
};
